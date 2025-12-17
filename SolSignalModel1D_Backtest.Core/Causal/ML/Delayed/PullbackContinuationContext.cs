using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using System;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Delayed
	{
	/// <summary>
	/// Контекст для диагностики pullback-сэмплов.
	/// Не участвует в ML.NET schema и не должен использоваться в тренере как вход в модель.
	/// </summary>
	public sealed class PullbackContinuationContext
		{
		public BacktestRecord Record { get; init; } = null!;

		public DateTime EntryUtc { get; init; }
		public bool GoLong { get; init; }
		public double DelayFactor { get; init; }

		public double EntryPrice12 { get; init; }
		public double MinMove { get; init; }

		public HourlyTradeResult BaseResult { get; init; }
		public double BaseTpPct { get; init; }
		public double BaseSlPct { get; init; }

		public bool DelayedExecuted { get; init; }
		public DelayedIntradayResult DelayedResult { get; init; }
		public double DelayedTpPct { get; init; }
		public double DelayedSlPct { get; init; }
		public DateTime? DelayedExecutedAtUtc { get; init; }
		public double TargetEntryPrice { get; init; }

		public bool Label { get; init; }

		public PullbackContinuationSample Sample { get; init; } = null!;
		}
	}
