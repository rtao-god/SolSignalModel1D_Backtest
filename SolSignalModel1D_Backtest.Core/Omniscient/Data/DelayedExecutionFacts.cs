using System;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Data
	{
	/// <summary>
	/// Факт исполнения delayed-входа (A/B).
	/// Строгая модель: отсутствие исполнения выражается null на уровне BacktestRecord.
	///
	/// Инварианты:
	/// - ExecutedAtUtc.Kind == Utc (окна/границы должны быть детерминированы).
	/// - EntryPrice > 0 и конечен (никаких "0.0 как заглушка").
	/// </summary>
	public sealed record DelayedExecutionFacts
		{
		public required DateTime ExecutedAtUtc { get; init; }
		public required double EntryPrice { get; init; }
		public required DelayedIntradayResult IntradayResult { get; init; }

		public static DelayedExecutionFacts Create (
			DateTime executedAtUtc,
			double entryPrice,
			DelayedIntradayResult intradayResult )
			{
			if (executedAtUtc.Kind != DateTimeKind.Utc)
				{
				throw new InvalidOperationException (
					$"[delayed] ExecutedAtUtc must be UTC, got Kind={executedAtUtc.Kind}, value={executedAtUtc:O}.");
				}

			if (double.IsNaN (entryPrice) || double.IsInfinity (entryPrice) || entryPrice <= 0.0)
				{
				throw new InvalidOperationException (
					$"[delayed] EntryPrice must be finite and > 0, got {entryPrice}.");
				}

			return new DelayedExecutionFacts
				{
				ExecutedAtUtc = executedAtUtc,
				EntryPrice = entryPrice,
				IntradayResult = intradayResult
				};
			}
		}
	}
