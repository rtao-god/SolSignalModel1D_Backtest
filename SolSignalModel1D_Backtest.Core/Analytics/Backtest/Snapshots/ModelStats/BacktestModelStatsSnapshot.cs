using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.Snapshots.ModelStats
	{
	/// <summary>
	/// Снимок модельных статистик бэктеста:
	/// - дневная путаница (3 класса);
	/// - путаница по тренду (DOWN vs UP);
	/// - SL-модель (confusion, метрики, sweep по порогам).
	/// </summary>
	public sealed class BacktestModelStatsSnapshot
		{
		public DateTime FromDateUtc { get; set; }
		public DateTime ToDateUtc { get; set; }

		public DailyConfusionStats Daily { get; set; } = new DailyConfusionStats ();
		public TrendDirectionStats Trend { get; set; } = new TrendDirectionStats ();
		public SlStats Sl { get; set; } = new SlStats ();
		}

	public sealed class DailyConfusionStats
		{
		public List<DailyClassStatsRow> Rows { get; } = new List<DailyClassStatsRow> ();
		public int OverallCorrect { get; set; }
		public int OverallTotal { get; set; }
		public double OverallAccuracyPct { get; set; }
		}

	public sealed class DailyClassStatsRow
		{
		public int TrueLabel { get; set; }
		public string LabelName { get; set; } = string.Empty;

		public int Pred0 { get; set; }
		public int Pred1 { get; set; }
		public int Pred2 { get; set; }

		public int Correct { get; set; }
		public int Total { get; set; }
		public double AccuracyPct { get; set; }
		}

	public sealed class TrendDirectionStats
		{
		public List<TrendDirectionStatsRow> Rows { get; } = new List<TrendDirectionStatsRow> ();
		public int OverallCorrect { get; set; }
		public int OverallTotal { get; set; }
		public double OverallAccuracyPct { get; set; }
		}

	public sealed class TrendDirectionStatsRow
		{
		public string Name { get; set; } = string.Empty;
		public int TrueIndex { get; set; }
		public int PredDown { get; set; }
		public int PredUp { get; set; }
		public int Correct { get; set; }
		public int Total { get; set; }
		public double AccuracyPct { get; set; }
		}

	public sealed class SlStats
		{
		public SlConfusionStats Confusion { get; set; } = new SlConfusionStats ();
		public SlMetricsStats Metrics { get; set; } = new SlMetricsStats ();
		public List<SlThresholdStatsRow> Thresholds { get; } = new List<SlThresholdStatsRow> ();
		}

	public sealed class SlConfusionStats
		{
		public int TpLow { get; set; }
		public int TpHigh { get; set; }
		public int SlLow { get; set; }
		public int SlHigh { get; set; }
		public int SlSaved { get; set; }

		public int TotalSignalDays { get; set; }
		public int ScoredDays { get; set; }
		public int TotalOutcomeDays { get; set; }

		public int TotalSlDays { get; set; }
		public int TotalTpDays { get; set; }
		}

	public sealed class SlMetricsStats
		{
		public double Coverage { get; set; }
		public double Tpr { get; set; }
		public double Fpr { get; set; }
		public double Precision { get; set; }
		public double Recall { get; set; }
		public double F1 { get; set; }
		public double PrAuc { get; set; }
		}

	public sealed class SlThresholdStatsRow
		{
		public double Threshold { get; set; }
		public double TprPct { get; set; }
		public double FprPct { get; set; }
		public double PredHighPct { get; set; }

		public int HighTotal { get; set; }
		public int TotalDays { get; set; }

		/// <summary>
		/// Условная "хорошесть" порога: TPR ≥ 60% и FPR ≤ 40%.
		/// </summary>
		public bool IsGood { get; set; }

		public int HighSlDays { get; set; }
		public int HighTpDays { get; set; }
		public int TotalSlDays { get; set; }
		public int TotalTpDays { get; set; }
		}
	}
