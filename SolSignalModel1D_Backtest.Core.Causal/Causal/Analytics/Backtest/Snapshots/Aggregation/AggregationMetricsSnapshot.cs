namespace SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Snapshots.Aggregation
{
	public sealed class AggregationMetricsSnapshot
		{
		public required int TotalInputRecords { get; init; }
		public required int ExcludedCount { get; init; }

		public required IReadOnlyList<AggregationMetricsSegmentSnapshot> Segments { get; init; }
		}

	public sealed class AggregationMetricsSegmentSnapshot
		{
		public required string SegmentName { get; init; }
		public required string SegmentLabel { get; init; }

		public required DateTime? FromDateUtc { get; init; }
		public required DateTime? ToDateUtc { get; init; }

		public required int RecordsCount { get; init; }

		public required LayerMetricsSnapshot Day { get; init; }
		public required LayerMetricsSnapshot DayMicro { get; init; }
		public required LayerMetricsSnapshot Total { get; init; }
		}

	public sealed class LayerMetricsSnapshot
		{
		public required string LayerName { get; init; }
		public required int[,] Confusion { get; init; }

		public required int N { get; init; }
		public required int Correct { get; init; }
		public required double Accuracy { get; init; }
		public required double MicroF1 { get; init; }

		public required double LogLoss { get; init; }
		public required int InvalidForLogLoss { get; init; }
		public required int ValidForLogLoss { get; init; }
		}
	}
