using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// Верхнеуровневый “дирижёр”, куда Program.cs/тесты передают уже готовые данные:
	/// mornings (NY-окна), records (PredictionRecord), 1m-свечи и политики плеча.
	/// Он получает BacktestConfig, конфигурирует RollingLoop и печатает модельные метрики.
	/// </summary>
	public sealed class BacktestRunner
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		/// <summary>
		/// Запускает бэктест по готовым данным и заранее собранным PolicySpec.
		/// Все "магические числа" (SL/TP, набор политик) приходят через BacktestConfig.
		/// Граница trainUntilUtc передаётся снаружи (из Program), чтобы Core не зависел
		/// от верхнего проекта.
		/// </summary>
		public void Run (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<PredictionRecord> records,
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

			// =====================================================================
			// Диагностика пути данных: диапазоны дат и расхождения mornings vs records.
			// Это не меняет поведение бэктеста, только даёт доп. лог в консоль.
			// =====================================================================

			// Диапазон и разбивка train/OOS по PredictionRecord
			int recordsCount = records.Count;
			int trainCount = 0;
			int oosCount = 0;
			DateTime? recMin = null;
			DateTime? recMax = null;

			for (int i = 0; i < recordsCount; i++)
				{
				var r = records[i];
				var d = r.DateUtc;

				if (!recMin.HasValue || d < recMin.Value)
					recMin = d;
				if (!recMax.HasValue || d > recMax.Value)
					recMax = d;

				if (d <= trainUntilUtc)
					trainCount++;
				else
					oosCount++;
				}

			if (recordsCount > 0)
				{
				Console.WriteLine (
					$"[diag-path] records: count={recordsCount}, " +
					$"range={recMin:yyyy-MM-dd}..{recMax:yyyy-MM-dd}");
				Console.WriteLine (
					$"[diag-path] trainUntil={trainUntilUtc:yyyy-MM-dd}, " +
					$"train={trainCount}, oos={oosCount}");
				}
			else
				{
				Console.WriteLine ("[diag-path] records: count=0");
				}

			// Диапазон по mornings (DataRow.Date)
			int morningsCount = mornings.Count;
			DateTime? mornMin = null;
			DateTime? mornMax = null;

			for (int i = 0; i < morningsCount; i++)
				{
				var d = mornings[i].Date;

				if (!mornMin.HasValue || d < mornMin.Value)
					mornMin = d;
				if (!mornMax.HasValue || d > mornMax.Value)
					mornMax = d;
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

			// Сравниваем множества календарных дат
			var recordDates = new HashSet<DateTime> ();
			for (int i = 0; i < recordsCount; i++)
				{
				recordDates.Add (records[i].DateUtc.Date);
				}

			var morningDates = new HashSet<DateTime> ();
			for (int i = 0; i < morningsCount; i++)
				{
				morningDates.Add (mornings[i].Date.Date);
				}

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

			if (recordOnly.Count > 10)
				recordOnly = recordOnly.GetRange (0, 10);
			if (morningOnly.Count > 10)
				morningOnly = morningOnly.GetRange (0, 10);

			Console.WriteLine ("[diag-path] dates in records but not in mornings (first 10):");
			if (recordOnly.Count == 0)
				{
				Console.WriteLine ("  (none)");
				}
			else
				{
				for (int i = 0; i < recordOnly.Count; i++)
					{
					Console.WriteLine ($"  {recordOnly[i]:yyyy-MM-dd}");
					}
				}

			Console.WriteLine ("[diag-path] dates in mornings but not in records (first 10):");
			if (morningOnly.Count == 0)
				{
				Console.WriteLine ("  (none)");
				}
			else
				{
				for (int i = 0; i < morningOnly.Count; i++)
					{
					Console.WriteLine ($"  {morningOnly[i]:yyyy-MM-dd}");
					}
				}

			// 1) Модельные метрики (дневная confusion + SL path-based, 1m)
			BacktestModelStatsPrinter.Print (
			   records,
			   candles1m,
			   config.DailyTpPct,
			   config.DailyStopPct,
			   NyTz,
			   trainUntilUtc
		   );

			// 2) Запуск AggregationProbsPrinter
			AggregationProbsPrinter.Print (
				records,
				trainUntilUtc,
				recentDays: 240,     // можно менять
				debugLastDays: 10    // сколько последних дней детализировать
			);

			// 3) Метрики accuracy / micro-F1 / logloss по Day / Day+Micro / Total
			AggregationMetricsPrinter.Print (
				records,
				trainUntilUtc,
				recentDays: 240
			);

			// 3) Запуск PnL/Delayed/окон по политикам
			/*var loop = new RollingLoop();
			loop.Run(
				mornings: mornings,
				records: records,
				candles1m: candles1m,
				policies: policies,
				config: config
			);

			DelayedStatsPrinter.Print(records);*/
			}
		}
	}
