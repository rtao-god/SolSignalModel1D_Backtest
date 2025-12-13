using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// Верхнеуровневый “дирижёр”, куда Program.cs/тесты передают уже готовые данные:
	/// mornings (NY-окна), records (BacktestRecord), 1m-свечи и политики плеча.
	/// Он получает BacktestConfig, конфигурирует RollingLoop и печатает модельные метрики.
	/// </summary>
	public sealed class BacktestRunner
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		public void Run (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<BacktestRecord> records,
			IReadOnlyList<Candle1m> candles1m,
			IReadOnlyList<RollingLoop.PolicySpec> policies,
			BacktestConfig config,
			DateTime trainUntilUtc )
			{
			if (mornings == null) throw new ArgumentNullException (nameof (mornings));
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (candles1m == null) throw new ArgumentNullException (nameof (candles1m));
			if (policies == null) throw new ArgumentNullException (nameof (policies));
			if (config == null) throw new ArgumentNullException (nameof (config));

			// ЕДИНСТВЕННЫЙ контракт train/OOS — через baseline-exit (TrainBoundary).
			// Это убирает "ручные" <=/> по датам и режет boundary leakage.
			var boundary = new TrainBoundary (trainUntilUtc, NyTz);

			// =====================================================================
			// Диагностика пути данных: диапазоны дат и расхождения mornings vs records.
			// =====================================================================

			int recordsCount = records.Count;

			DateTime? recMin = null;
			DateTime? recMax = null;

			var recordDates = new HashSet<DateTime> ();
			var causalRecords = new List<CausalPredictionRecord> (recordsCount);

			for (int i = 0; i < recordsCount; i++)
				{
				var r = records[i];

				var c = r.Causal;
				if (c == null)
					{
					throw new InvalidOperationException (
						$"[BacktestRunner] records[{i}].Causal is null.");
					}

				causalRecords.Add (c);

				var d = c.DateUtc;

				if (!recMin.HasValue || d < recMin.Value) recMin = d;
				if (!recMax.HasValue || d > recMax.Value) recMax = d;

				recordDates.Add (d.Date);
				}

			if (recordsCount > 0)
				{
				Console.WriteLine (
					$"[diag-path] records: count={recordsCount}, " +
					$"range={recMin:yyyy-MM-dd}..{recMax:yyyy-MM-dd}");
				}
			else
				{
				Console.WriteLine ("[diag-path] records: count=0");
				}

			// Разбиение records по boundary (exit<=trainUntil / exit>trainUntil / excluded).
			var splitRecords = boundary.Split (records, r => r.Causal.DateUtc);

			Console.WriteLine (
				$"[diag-path] boundary trainUntil={boundary.TrainUntilIsoDate}, " +
				$"train={splitRecords.Train.Count}, oos={splitRecords.Oos.Count}, excluded={splitRecords.Excluded.Count}");

			// Диапазон по mornings (DataRow.Date)
			int morningsCount = mornings.Count;
			DateTime? mornMin = null;
			DateTime? mornMax = null;

			var morningDates = new HashSet<DateTime> ();

			for (int i = 0; i < morningsCount; i++)
				{
				var d = mornings[i].Date;

				if (!mornMin.HasValue || d < mornMin.Value) mornMin = d;
				if (!mornMax.HasValue || d > mornMax.Value) mornMax = d;

				morningDates.Add (d.Date);
				}

			if (morningsCount > 0)
				{
				Console.WriteLine (
					$"[diag-path] mornings: count={morningsCount}, " +
					$"range={mornMin:yyyy-MM-dd}..{mornMax:yyyy-MM-dd}");
				}
			else
				{
				Console.WriteLine ("[diag-path] mornings: count=0");
				}

			// Сравниваем множества календарных дат (помогает ловить "дырки" и смещения пайплайна).
			var recordOnly = new List<DateTime> ();
			foreach (var d in recordDates)
				{
				if (!morningDates.Contains (d))
					recordOnly.Add (d);
				}
			recordOnly.Sort ();

			var morningOnly = new List<DateTime> ();
			foreach (var d in morningDates)
				{
				if (!recordDates.Contains (d))
					morningOnly.Add (d);
				}
			morningOnly.Sort ();

			if (recordOnly.Count > 10) recordOnly = recordOnly.GetRange (0, 10);
			if (morningOnly.Count > 10) morningOnly = morningOnly.GetRange (0, 10);

			Console.WriteLine ("[diag-path] dates in records but not in mornings (first 10):");
			if (recordOnly.Count == 0) Console.WriteLine ("  (none)");
			else
				{
				for (int i = 0; i < recordOnly.Count; i++)
					Console.WriteLine ($"  {recordOnly[i]:yyyy-MM-dd}");
				}

			Console.WriteLine ("[diag-path] dates in mornings but not in records (first 10):");
			if (morningOnly.Count == 0) Console.WriteLine ("  (none)");
			else
				{
				for (int i = 0; i < morningOnly.Count; i++)
					Console.WriteLine ($"  {morningOnly[i]:yyyy-MM-dd}");
				}

			// =====================================================================
			// Каузальная аналитика (не должна зависеть от forward и 1m-path).
			// =====================================================================

			AggregationProbsPrinter.Print (
				causalRecords,
				boundary,
				recentDays: 240,
				debugLastDays: 10);

			AggregationMetricsPrinter.Print (
				causalRecords,
				boundary,
				recentDays: 240);

			// =====================================================================
			// Omniscient-аналитика (BacktestRecord + 1m)
			// =====================================================================

			BacktestModelStatsPrinter.Print (
				records,
				candles1m,
				config.DailyTpPct,
				config.DailyStopPct,
				NyTz,
				trainUntilUtc);

			// 4) Запуск PnL/Delayed/окон по политикам — оставлен закомментированным
			/*
			var loop = new RollingLoop();
			loop.Run(
				mornings: mornings,
				records: records,
				candles1m: candles1m,
				policies: policies,
				config: config
			);

			DelayedStatsPrinter.Print(records);
			*/
			}
		}
	}
