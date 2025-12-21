using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Adapters;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Printers;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.Aggregation;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.Micro;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// Верхнеуровневый “дирижёр”: принимает готовые данные и запускает каузальную аналитику/бек-тест.
	/// </summary>
	public sealed class BacktestRunner
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		public void Run (
			IReadOnlyList<LabeledCausalRow> mornings,
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

			var boundary = new TrainBoundary (trainUntilUtc, NyTz);

			// ===== records coverage =====
			int recordsCount = records.Count;

			DateTime? recMin = null;
			DateTime? recMax = null;

			var recordDays = new HashSet<DateTime> (recordsCount);

			for (int i = 0; i < recordsCount; i++)
				{
				var r = records[i];
				var day = CausalTimeKey.DayKeyUtc (r);

				if (!recMin.HasValue || day < recMin.Value) recMin = day;
				if (!recMax.HasValue || day > recMax.Value) recMax = day;

				recordDays.Add (day);
				}

			if (recordsCount > 0)
				Console.WriteLine ($"[diag-path] records: count={recordsCount}, range={recMin:yyyy-MM-dd}..{recMax:yyyy-MM-dd}");
			else
				Console.WriteLine ("[diag-path] records: count=0");

			var splitRecords = boundary.Split (records, r => CausalTimeKey.DayKeyUtc (r));

			Console.WriteLine (
				$"[diag-path] boundary trainUntil={boundary.TrainUntilIsoDate}, " +
				$"train={splitRecords.Train.Count}, oos={splitRecords.Oos.Count}, excluded={splitRecords.Excluded.Count}");

			// ===== mornings coverage =====
			int morningsCount = mornings.Count;
			DateTime? mornMin = null;
			DateTime? mornMax = null;

			var morningDays = new HashSet<DateTime> (morningsCount);

			for (int i = 0; i < morningsCount; i++)
				{
				var day = CausalTimeKey.DayKeyUtc (mornings[i]);

				if (!mornMin.HasValue || day < mornMin.Value) mornMin = day;
				if (!mornMax.HasValue || day > mornMax.Value) mornMax = day;

				morningDays.Add (day);
				}

			if (morningsCount > 0)
				Console.WriteLine ($"[diag-path] mornings: count={morningsCount}, range={mornMin:yyyy-MM-dd}..{mornMax:yyyy-MM-dd}");
			else
				Console.WriteLine ("[diag-path] mornings: count=0");

			// ===== mismatch sample =====
			var recordOnly = new List<DateTime> ();
			foreach (var d in recordDays)
				{
				if (!morningDays.Contains (d))
					recordOnly.Add (d);
				}
			recordOnly.Sort ();

			var morningOnly = new List<DateTime> ();
			foreach (var d in morningDays)
				{
				if (!recordDays.Contains (d))
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

			// ===== Каузальная аналитика (не должна зависеть от forward и 1m-path) =====
			var rows = records.Select (r => r.ToAggRow ()).ToList ();

			var probsSnap = AggregationProbsSnapshotBuilder.Build (rows, boundary, recentDays: 240, debugLastDays: 10);
			AggregationProbsPrinter.Print (probsSnap);

			var metricsSnap = AggregationMetricsSnapshotBuilder.Build (rows, boundary, recentDays: 240);
			AggregationMetricsPrinter.Print (metricsSnap);

			var microSnap = MicroStatsSnapshotBuilder.Build (rows);
			MicroStatsPrinter.Print (microSnap);

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
