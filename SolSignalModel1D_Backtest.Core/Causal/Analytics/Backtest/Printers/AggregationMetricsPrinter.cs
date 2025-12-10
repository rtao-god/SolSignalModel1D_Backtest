using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Метрики по агрегации вероятностей:
	/// для каждого сегмента (Train/OOS/Recent/Full) считает:
	/// - confusion 3×3 и accuracy / micro-F1 / logloss
	/// для трёх слоёв:
	///   - Day          (PredLabel_Day, Prob*_Day),
	///   - Day+Micro    (PredLabel_DayMicro, Prob*_DayMicro),
	///   - Total        (PredLabel, Prob*_Total).
	/// </summary>
	public static class AggregationMetricsPrinter
		{
		public static void Print (
			IReadOnlyList<CausalPredictionRecord> records,
			DateTime trainUntilUtc,
			int recentDays = 240 )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (records.Count == 0)
				{
				ConsoleStyler.WriteHeader ("==== AGGREGATION METRICS ====");
				Console.WriteLine ("[agg-metrics] no records, nothing to print.");
				return;
				}

			if (recentDays <= 0) recentDays = 1;

			ConsoleStyler.WriteHeader ("==== AGGREGATION METRICS ====");

			// Стабильный порядок по дате.
			var ordered = records
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var minDateUtc = ordered.First ().DateUtc;
			var maxDateUtc = ordered.Last ().DateUtc;

			Console.WriteLine (
				$"[agg-metrics] full records period = {minDateUtc:yyyy-MM-dd}..{maxDateUtc:yyyy-MM-dd}, totalRecords = {ordered.Count}");

			// Сегменты такие же, как в AggregationProbsPrinter.
			var train = ordered
				.Where (r => r.DateUtc <= trainUntilUtc)
				.ToList ();

			var oos = ordered
				.Where (r => r.DateUtc > trainUntilUtc)
				.ToList ();

			var full = ordered;

			var fromRecentUtc = maxDateUtc.AddDays (-recentDays);
			var recent = ordered
				.Where (r => r.DateUtc >= fromRecentUtc)
				.ToList ();

			if (recent.Count == 0)
				{
				recent = full;
				}

			var meta = new TextTable ();
			meta.AddHeader ("segment", "from", "to", "days");
			AddSegmentMetaRow (meta, "Train", train);
			AddSegmentMetaRow (meta, "OOS", oos);
			AddSegmentMetaRow (meta, $"Recent({recentDays}d)", recent);
			AddSegmentMetaRow (meta, "Full", full);
			meta.WriteToConsole ();
			Console.WriteLine ();

			PrintSegmentMetrics ("Train", train);
			PrintSegmentMetrics ("OOS", oos);
			PrintSegmentMetrics ($"Recent (last {recentDays} days)", recent);
			PrintSegmentMetrics ("Full history", full);
			}

		private static void AddSegmentMetaRow ( TextTable t, string name, IReadOnlyList<CausalPredictionRecord> seg )
			{
			if (seg == null || seg.Count == 0)
				{
				t.AddRow (name, "-", "-", "0");
				return;
				}

			var from = seg.First ().DateUtc;
			var to = seg.Last ().DateUtc;

			t.AddRow (
				name,
				from.ToString ("yyyy-MM-dd"),
				to.ToString ("yyyy-MM-dd"),
				seg.Count.ToString ());
			}

		// =====================================================================
		// Метрики по сегменту
		// =====================================================================

		private sealed class LayerMetrics
			{
			public string LayerName { get; init; } = string.Empty;
			public int[,] Confusion { get; init; } = new int[3, 3];

			/// <summary>Всего объектов в сегменте.</summary>
			public int N { get; init; }

			/// <summary>Сколько предсказаний совпало с TrueLabel.</summary>
			public int Correct { get; init; }

			/// <summary>Точность (accuracy) по всем объектам.</summary>
			public double Accuracy { get; init; }

			/// <summary>Микро-F1. Для single-label multi-class равен accuracy.</summary>
			public double MicroF1 { get; init; }

			/// <summary>Средний logloss по тем объектам, где p_true &gt; 0. Иначе NaN.</summary>
			public double LogLoss { get; init; }

			/// <summary>Сколько объектов вообще нельзя использовать для logloss (p_true == 0).</summary>
			public int InvalidForLogLoss { get; init; }

			/// <summary>Сколько объектов реально участвуют в logloss.</summary>
			public int ValidForLogLoss { get; init; }
			}

		private static void PrintSegmentMetrics ( string title, IReadOnlyList<CausalPredictionRecord> seg )
			{
			ConsoleStyler.WriteHeader ($"[agg-metrics] {title}");

			if (seg == null || seg.Count == 0)
				{
				Console.WriteLine ("[agg-metrics] segment is empty.");
				Console.WriteLine ();
				return;
				}

			var day = ComputeLayerMetrics (
				seg,
				"Day",
				r => r.PredLabel_Day,
				r => (r.ProbUp_Day, r.ProbFlat_Day, r.ProbDown_Day));

			var dayMicro = ComputeLayerMetrics (
				seg,
				"Day+Micro",
				r => r.PredLabel_DayMicro,
				r => (r.ProbUp_DayMicro, r.ProbFlat_DayMicro, r.ProbDown_DayMicro));

			var total = ComputeLayerMetrics (
				seg,
				"Total",
				r => r.PredLabel,
				r => (r.ProbUp_Total, r.ProbFlat_Total, r.ProbDown_Total));

			PrintLayerMetrics (day);
			PrintLayerMetrics (dayMicro);
			PrintLayerMetrics (total);

			Console.WriteLine ();
			}

		private static LayerMetrics ComputeLayerMetrics (
			IReadOnlyList<CausalPredictionRecord> seg,
			string layerName,
			Func<CausalPredictionRecord, int> predSelector,
			Func<CausalPredictionRecord, (double up, double flat, double down)> probSelector )
			{
			var conf = new int[3, 3];
			int n = seg.Count;
			int correct = 0;
			double sumLog = 0.0;

			int invalidForLogLoss = 0;
			int validForLogLoss = 0;

			foreach (var r in seg)
				{
				int y = r.TrueLabel;
				if (y < 0 || y > 2)
					{
					throw new InvalidOperationException (
						$"[agg-metrics] Unexpected TrueLabel={y} for date {r.DateUtc:O}. Expected 0/1/2.");
					}

				int pred = predSelector (r);
				if (pred < 0 || pred > 2)
					{
					throw new InvalidOperationException (
						$"[agg-metrics] Unexpected predicted label={pred} in layer '{layerName}' for date {r.DateUtc:O}. Expected 0/1/2.");
					}

				var (pUp, pFlat, pDown) = probSelector (r);

				if (pUp < 0.0 || pFlat < 0.0 || pDown < 0.0)
					{
					throw new InvalidOperationException (
						$"[agg-metrics] Negative probability in layer '{layerName}' for date {r.DateUtc:O}. " +
						$"P_up={pUp}, P_flat={pFlat}, P_down={pDown}.");
					}

				double sum = pUp + pFlat + pDown;
				if (sum <= 0.0)
					{
					throw new InvalidOperationException (
						$"[agg-metrics] Degenerate probability triple (sum<=0) in layer '{layerName}' for date {r.DateUtc:O}. " +
						$"P_up={pUp}, P_flat={pFlat}, P_down={pDown}.");
					}

				double pTrue = y switch
					{
						2 => pUp,
						1 => pFlat,
						0 => pDown,
						_ => throw new InvalidOperationException ("Unreachable label branch")
						};

				conf[y, pred]++;

				if (pred == y)
					{
					correct++;
					}

				if (pTrue <= 0.0)
					{
					invalidForLogLoss++;
					}
				else
					{
					validForLogLoss++;
					sumLog += Math.Log (pTrue);
					}
				}

			double accuracy = n > 0 ? (double) correct / n : double.NaN;
			double microF1 = accuracy;

			double logLoss;
			if (validForLogLoss == 0)
				{
				logLoss = double.NaN;
				}
			else
				{
				logLoss = -sumLog / validForLogLoss;
				}

			return new LayerMetrics
				{
				LayerName = layerName,
				Confusion = conf,
				N = n,
				Correct = correct,
				Accuracy = accuracy,
				MicroF1 = microF1,
				LogLoss = logLoss,
				InvalidForLogLoss = invalidForLogLoss,
				ValidForLogLoss = validForLogLoss
				};
			}

		private static void PrintLayerMetrics ( LayerMetrics m )
			{
			ConsoleStyler.WriteHeader ($"[agg-metrics] layer = {m.LayerName}");

			var cm = new TextTable ();
			cm.AddHeader ("true\\pred", "0", "1", "2", "rowSum");

			int row0 = m.Confusion[0, 0] + m.Confusion[0, 1] + m.Confusion[0, 2];
			int row1 = m.Confusion[1, 0] + m.Confusion[1, 1] + m.Confusion[1, 2];
			int row2 = m.Confusion[2, 0] + m.Confusion[2, 1] + m.Confusion[2, 2];

			cm.AddRow (
				"0",
				m.Confusion[0, 0].ToString (),
				m.Confusion[0, 1].ToString (),
				m.Confusion[0, 2].ToString (),
				row0.ToString ());

			cm.AddRow (
				"1",
				m.Confusion[1, 0].ToString (),
				m.Confusion[1, 1].ToString (),
				m.Confusion[1, 2].ToString (),
				row1.ToString ());

			cm.AddRow (
				"2",
				m.Confusion[2, 0].ToString (),
				m.Confusion[2, 1].ToString (),
				m.Confusion[2, 2].ToString (),
				row2.ToString ());

			int col0 = m.Confusion[0, 0] + m.Confusion[1, 0] + m.Confusion[2, 0];
			int col1 = m.Confusion[0, 1] + m.Confusion[1, 1] + m.Confusion[2, 1];
			int col2 = m.Confusion[0, 2] + m.Confusion[1, 2] + m.Confusion[2, 2];

			cm.AddRow (
				"colSum",
				col0.ToString (),
				col1.ToString (),
				col2.ToString (),
				m.N.ToString ());

			cm.WriteToConsole ();
			Console.WriteLine ();

			var metrics = new TextTable ();
			metrics.AddHeader ("metric", "value");
			metrics.AddRow ("N", m.N.ToString ());
			metrics.AddRow ("accuracy", m.Accuracy.ToString ("0.000"));
			metrics.AddRow ("micro-F1", m.MicroF1.ToString ("0.000"));
			metrics.AddRow ("logloss", double.IsNaN (m.LogLoss) ? "NaN" : m.LogLoss.ToString ("0.000"));
			metrics.AddRow ("logloss_valid_days", m.ValidForLogLoss.ToString ());
			metrics.AddRow ("logloss_invalid_days(p_true=0)", m.InvalidForLogLoss.ToString ());
			metrics.WriteToConsole ();

			Console.WriteLine ();

			if (m.InvalidForLogLoss > 0)
				{
				Console.WriteLine (
					$"[agg-metrics] WARNING: layer '{m.LayerName}' has {m.InvalidForLogLoss} records " +
					"with p_true=0; они не учитываются в logloss, этот показатель отражает только " +
					$"{m.ValidForLogLoss} дней с p_true>0.");
				Console.WriteLine ();
				}
			}
		}
	}
