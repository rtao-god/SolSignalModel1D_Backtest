using System;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	public sealed class PredictionRecord
		{
		public DateTime DateUtc { get; set; }
		public int TrueLabel { get; set; }
		public int PredLabel { get; set; }

		public bool PredMicroUp { get; set; }
		public bool PredMicroDown { get; set; }

		public bool FactMicroUp { get; set; }
		public bool FactMicroDown { get; set; }

		public double Entry { get; set; }
		public double MaxHigh24 { get; set; }
		public double MinLow24 { get; set; }
		public double Close24 { get; set; }

		public bool RegimeDown { get; set; }

		public string Reason { get; set; } = string.Empty;

		public double MinMove { get; set; }

		// ==== delayed A/B ====
		public string? DelayedSource { get; set; }
		public bool DelayedEntryUsed { get; set; }
		public bool DelayedEntryExecuted { get; set; }
		public double DelayedEntryPrice { get; set; }
		public int DelayedIntradayResult { get; set; }
		public double DelayedIntradayTpPct { get; set; }
		public double DelayedIntradaySlPct { get; set; }
		public int TargetLevelClass { get; set; }

		// вероятность SL, если ты её считаешь
		public double SlProb { get; set; }

		/// <summary>
		/// Что в онлайне сказала SL-модель по этому дню: true = high risk, false = low.
		/// Это именно runtime-решение, а не просто вероятность.
		/// </summary>
		public bool SlHighDecision { get; set; }

		/// <summary>
		/// Фактическое время исполнения delayed (если исполнилось).
		/// Нужно, чтобы PnL не проверял фитили до входа.
		/// </summary>
		public DateTime? DelayedEntryExecutedAtUtc { get; set; }
		}
	}
