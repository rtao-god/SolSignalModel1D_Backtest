using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.ModelStats
	{
	/// <summary>
	/// Снимок модельных статистик бэктеста:
	/// - дневная путаница (3 класса);
	/// - путаница по тренду (DOWN vs UP);
	/// - SL-модель (confusion, метрики, sweep по порогам).
	/// </summary>
	public sealed class BacktestModelStatsSnapshot
		{
		public required DateTime FromDateUtc { get; init; }
		public required DateTime ToDateUtc { get; init; }

		public required DailyConfusionStats Daily { get; init; }
		public required TrendDirectionStats Trend { get; init; }
		public required OptionalValue<SlStats> Sl { get; init; }
		}

	public sealed class DailyConfusionStats
		{
		public required IReadOnlyList<DailyClassStatsRow> Rows { get; init; }
		public required int OverallCorrect { get; init; }
		public required int OverallTotal { get; init; }
		public required double OverallAccuracyPct { get; init; }
		}

	public sealed class DailyClassStatsRow
		{
		public required int TrueLabel { get; init; }
		public required string LabelName { get; init; }

		public required int Pred0 { get; init; }
		public required int Pred1 { get; init; }
		public required int Pred2 { get; init; }

		public required int Correct { get; init; }
		public required int Total { get; init; }
		public required double AccuracyPct { get; init; }
		}

	public sealed class TrendDirectionStats
		{
		public required IReadOnlyList<TrendDirectionStatsRow> Rows { get; init; }
		public required int OverallCorrect { get; init; }
		public required int OverallTotal { get; init; }
		public required double OverallAccuracyPct { get; init; }
		}

	public sealed class TrendDirectionStatsRow
		{
		public required string Name { get; init; }
		public required int TrueIndex { get; init; }
		public required int PredDown { get; init; }
		public required int PredUp { get; init; }
		public required int Correct { get; init; }
		public required int Total { get; init; }
		public required double AccuracyPct { get; init; }
		}

	public sealed class SlStats
		{
		public required SlConfusionStats Confusion { get; init; }
		public required SlMetricsStats Metrics { get; init; }
		public required IReadOnlyList<SlThresholdStatsRow> Thresholds { get; init; }
		}

	public sealed class SlConfusionStats
		{
		public required int TpLow { get; init; }
		public required int TpHigh { get; init; }
		public required int SlLow { get; init; }
		public required int SlHigh { get; init; }
		public required int SlSaved { get; init; }

		public required int TotalSignalDays { get; init; }
		public required int ScoredDays { get; init; }
		public required int TotalOutcomeDays { get; init; }

		public required int TotalSlDays { get; init; }
		public required int TotalTpDays { get; init; }
		}

	public sealed class SlMetricsStats
		{
		public required double Coverage { get; init; }
		public required double Tpr { get; init; }
		public required double Fpr { get; init; }
		public required double Precision { get; init; }
		public required double Recall { get; init; }
		public required double F1 { get; init; }
		public required double PrAuc { get; init; }
		}

	public sealed class SlThresholdStatsRow
		{
		public required double Threshold { get; init; }
		public required double TprPct { get; init; }
		public required double FprPct { get; init; }
		public required double PredHighPct { get; init; }

		public required int HighTotal { get; init; }
		public required int TotalDays { get; init; }

		/// <summary>
		/// Условная "хорошесть" порога: TPR ≥ 60% и FPR ≤ 40%.
		/// </summary>
		public required bool IsGood { get; init; }

		public required int HighSlDays { get; init; }
		public required int HighTpDays { get; init; }
		public required int TotalSlDays { get; init; }
		public required int TotalTpDays { get; init; }
		}
	}
