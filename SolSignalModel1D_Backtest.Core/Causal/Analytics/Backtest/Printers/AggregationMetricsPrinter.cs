using System;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.Aggregation;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Printers
	{
	public static class AggregationMetricsPrinter
		{
		public static void Print ( AggregationMetricsSnapshot snapshot )
			{
			if (snapshot == null) throw new ArgumentNullException (nameof (snapshot));

			ConsoleStyler.WriteHeader ("==== AGGREGATION METRICS ====");

			if (snapshot.TotalInputRecords == 0)
				{
				Console.WriteLine ("[agg-metrics] no records, nothing to print.");
				return;
				}

			if (snapshot.ExcludedCount > 0)
				{
				Console.WriteLine (
					$"[agg-metrics][WARN] excluded (no baseline-exit) = {snapshot.ExcludedCount}. " +
					"Проверь контракт entryUtc/окна.");
				}

			var meta = new TextTable ();
			meta.AddHeader ("segment", "from", "to", "days");

			foreach (var seg in snapshot.Segments)
				{
				if (seg.RecordsCount == 0)
					{
					meta.AddRow (seg.SegmentLabel, "-", "-", "0");
					continue;
					}

				meta.AddRow (
					seg.SegmentLabel,
					seg.FromDateUtc!.Value.ToString ("yyyy-MM-dd"),
					seg.ToDateUtc!.Value.ToString ("yyyy-MM-dd"),
					seg.RecordsCount.ToString ());
				}

			meta.WriteToConsole ();
			Console.WriteLine ();

			foreach (var seg in snapshot.Segments)
				{
				PrintSegment (seg);
				}
			}

		private static void PrintSegment ( AggregationMetricsSegmentSnapshot seg )
			{
			ConsoleStyler.WriteHeader ($"[agg-metrics] {seg.SegmentName}");

			if (seg.RecordsCount == 0)
				{
				Console.WriteLine ("[agg-metrics] segment is empty.");
				Console.WriteLine ();
				return;
				}

			PrintLayer (seg.Day);
			PrintLayer (seg.DayMicro);
			PrintLayer (seg.Total);

			Console.WriteLine ();
			}

		private static void PrintLayer ( LayerMetricsSnapshot m )
			{
			ConsoleStyler.WriteHeader ($"[agg-metrics] layer = {m.LayerName}");

			var cm = new TextTable ();
			cm.AddHeader ("true\\pred", "0", "1", "2", "rowSum");

			int row0 = m.Confusion[0, 0] + m.Confusion[0, 1] + m.Confusion[0, 2];
			int row1 = m.Confusion[1, 0] + m.Confusion[1, 1] + m.Confusion[1, 2];
			int row2 = m.Confusion[2, 0] + m.Confusion[2, 1] + m.Confusion[2, 2];

			cm.AddRow ("0", m.Confusion[0, 0].ToString (), m.Confusion[0, 1].ToString (), m.Confusion[0, 2].ToString (), row0.ToString ());
			cm.AddRow ("1", m.Confusion[1, 0].ToString (), m.Confusion[1, 1].ToString (), m.Confusion[1, 2].ToString (), row1.ToString ());
			cm.AddRow ("2", m.Confusion[2, 0].ToString (), m.Confusion[2, 1].ToString (), m.Confusion[2, 2].ToString (), row2.ToString ());

			int col0 = m.Confusion[0, 0] + m.Confusion[1, 0] + m.Confusion[2, 0];
			int col1 = m.Confusion[0, 1] + m.Confusion[1, 1] + m.Confusion[2, 1];
			int col2 = m.Confusion[0, 2] + m.Confusion[1, 2] + m.Confusion[2, 2];

			cm.AddRow ("colSum", col0.ToString (), col1.ToString (), col2.ToString (), m.N.ToString ());

			cm.WriteToConsole ();
			Console.WriteLine ();

			var metrics = new TextTable ();
			metrics.AddHeader ("metric", "value");
			metrics.AddRow ("N", m.N.ToString ());
			metrics.AddRow ("accuracy", double.IsNaN (m.Accuracy) ? "NaN" : m.Accuracy.ToString ("0.000"));
			metrics.AddRow ("micro-F1", double.IsNaN (m.MicroF1) ? "NaN" : m.MicroF1.ToString ("0.000"));
			metrics.AddRow ("logloss", double.IsNaN (m.LogLoss) ? "NaN" : m.LogLoss.ToString ("0.000"));
			metrics.AddRow ("logloss_valid_days", m.ValidForLogLoss.ToString ());
			metrics.AddRow ("logloss_invalid_days(p_true=0)", m.InvalidForLogLoss.ToString ());
			metrics.WriteToConsole ();

			Console.WriteLine ();

			if (m.InvalidForLogLoss > 0)
				{
				Console.WriteLine (
					$"[agg-metrics] WARNING: layer '{m.LayerName}' has {m.InvalidForLogLoss} records with p_true=0; " +
					"они не учитываются в logloss.");
				Console.WriteLine ();
				}
			}
		}
	}
