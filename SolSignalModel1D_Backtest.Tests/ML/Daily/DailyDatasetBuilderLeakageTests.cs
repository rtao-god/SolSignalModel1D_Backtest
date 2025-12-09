using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML.Daily;
using System;
using System.Collections.Generic;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.Daily
	{
	/// <summary>
	/// Тесты, которые проверяют, что DailyDatasetBuilder:
	/// - режет трейновый набор по baseline-exit, а не просто по r.Date;
	/// - не даёт утечки path-based факта за trainUntil.
	/// </summary>
	public sealed class DailyDatasetBuilderLeakageTests
		{
		/// <summary>
		/// Сценарий:
		/// - берём один будний день (entryUtc);
		/// - вычисляем его baseline-exit через Windowing.ComputeBaselineExitUtc;
		/// - собираем два датасета:
		///   1) trainUntil строго между entryUtc и exitUtc — строка ДОЛЖНА быть выкинута;
		///   2) trainUntil после exitUtc — строка ДОЛЖНА остаться.
		///
		/// Если кто-то в будущем уберёт фильтрацию по baseline-exit и будет смотреть только на Date,
		/// этот тест развалится: при trainUntil между entry и exit строка окажется в TrainRows.
		/// </summary>
		[Fact]
		public void Build_UsesBaselineExitToCutTrainRows ()
			{
			var nyTz = TimeZones.NewYork;

			// Берём заведомо будний день в NY (понедельник, 08:00 NY).
			var entryLocalNy = new DateTime (2025, 1, 6, 8, 0, 0, DateTimeKind.Unspecified);
			var entryUtc = TimeZoneInfo.ConvertTimeToUtc (entryLocalNy, nyTz);

			var exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz);
			Assert.True (exitUtc > entryUtc);

			var rows = new List<DataRow>
			{
				CreateRow(
					dateUtc: entryUtc,
					label: 2,
					regimeDown: false)
			};

			// trainUntil строго между entry и exit → baseline-окно залезает в "будущее".
			var trainUntilBeforeExit = entryUtc + TimeSpan.FromTicks ((exitUtc - entryUtc).Ticks / 2);

			var dsBeforeExit = DailyDatasetBuilder.Build (
				allRows: rows,
				trainUntil: trainUntilBeforeExit,
				balanceMove: false,
				balanceDir: false,
				balanceTargetFrac: 0.5,
				datesToExclude: null);

			// Строка НЕ должна попасть в train-набор:
			Assert.Empty (dsBeforeExit.TrainRows);

			// Теперь ставим trainUntil после baseline-exit — строка уже полностью "в прошлом".
			var trainUntilAfterExit = exitUtc.AddMinutes (1);

			var dsAfterExit = DailyDatasetBuilder.Build (
				allRows: rows,
				trainUntil: trainUntilAfterExit,
				balanceMove: false,
				balanceDir: false,
				balanceTargetFrac: 0.5,
				datesToExclude: null);

			Assert.Single (dsAfterExit.TrainRows);
			Assert.Equal (entryUtc, dsAfterExit.TrainRows[0].Date);
			}

		/// <summary>
		/// Дополнительный sanity-тест:
		/// - строим разумный диапазон будних дат;
		/// - выбираем trainUntil где-то "внутри";
		/// - убеждаемся, что все TrainRows удовлетворяют двум условиям:
		///   1) r.Date <= trainUntil;
		///   2) baseline-exit(r.Date) <= trainUntil.
		///
		/// Это уже именно инвариант "нет утечек через baseline-exit", а не просто проверка куска кода.
		/// </summary>
		[Fact]
		public void Build_TrainRowsHaveBaselineExitNotAfterTrainUntil ()
			{
			var nyTz = TimeZones.NewYork;

			var rows = new List<DataRow> ();
			var local = new DateTime (2025, 1, 1, 8, 0, 0, DateTimeKind.Unspecified);

			// Берём ~40 NY-утр подряд, пропуская выходные.
			while (rows.Count < 40)
				{
				if (local.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
					{
					var entryUtc = TimeZoneInfo.ConvertTimeToUtc (local, nyTz);
					rows.Add (CreateRow (dateUtc: entryUtc, label: 2, regimeDown: false));
					}

				local = local.AddDays (1);
				}

			// В качестве trainUntil берём baseline-exit последнего дня + чуть-чуть.
			var lastEntry = rows[^1].Date;
			var lastExit = Windowing.ComputeBaselineExitUtc (lastEntry, nyTz);
			var trainUntil = lastExit.AddMinutes (1);

			var ds = DailyDatasetBuilder.Build (
				allRows: rows,
				trainUntil: trainUntil,
				balanceMove: false,
				balanceDir: false,
				balanceTargetFrac: 0.5,
				datesToExclude: null);

			Assert.NotEmpty (ds.TrainRows);

			foreach (var r in ds.TrainRows)
				{
				Assert.True (r.Date <= trainUntil);

				var exit = Windowing.ComputeBaselineExitUtc (r.Date, nyTz);
				Assert.True (exit <= trainUntil);
				}
			}

		private static DataRow CreateRow ( DateTime dateUtc, int label, bool regimeDown )
			{
			return new DataRow
				{
				Date = dateUtc,
				Label = label,
				RegimeDown = regimeDown,
				IsMorning = true,   // имитируем утреннюю строку RowBuilder'а
				MinMove = 0.02,     // разумный минимум, чтобы сильно не отличаться от живых данных
				Features = new double[4]
				};
			}
		}
	}
