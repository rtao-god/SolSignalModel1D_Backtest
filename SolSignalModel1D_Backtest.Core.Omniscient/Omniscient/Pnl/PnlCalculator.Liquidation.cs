using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl
	{
	public static partial class PnlCalculator
		{
		private const double MaintenanceMarginRate = 0.004;
		private const double BacktestLiqAdverseMultiplier = 0.97;

		private static double ComputeLiqAdversePct ( double leverage )
			{
			if (leverage <= 0.0)
				throw new InvalidOperationException ("[pnl] leverage must be positive in ComputeLiqAdversePct().");

			double liqAdversePct = 1.0 / leverage - MaintenanceMarginRate;

			// Если <= 0 — это конфиг/плечо несовместимы с MMR, править “фолбэком” нельзя.
			if (liqAdversePct <= 0.0)
				{
				throw new InvalidOperationException (
					$"[pnl] invalid liquidation distance: IMR(=1/leverage) <= MMR. " +
					$"leverage={leverage:0.###}, IMR={1.0 / leverage:0.######}, MMR={MaintenanceMarginRate:0.######}");
				}

			return liqAdversePct;
			}

		private static double ComputeBacktestLiqAdversePct ( double leverage )
			{
			double basePct = ComputeLiqAdversePct (leverage);
			double pct = basePct * BacktestLiqAdverseMultiplier;

			if (pct <= 0.0)
				throw new InvalidOperationException ("[pnl] backtest liquidation distance must be positive.");

			return pct;
			}

		private static double ComputeLiqPrice ( double entry, bool isLong, double leverage )
			{
			if (entry <= 0.0)
				throw new InvalidOperationException ("[pnl] entry must be positive in ComputeLiqPrice().");

			double pct = ComputeLiqAdversePct (leverage);

			return isLong
				? entry * (1.0 - pct)
				: entry * (1.0 + pct);
			}

		private static double ComputeBacktestLiqPrice ( double entry, bool isLong, double leverage )
			{
			if (entry <= 0.0)
				throw new InvalidOperationException ("[pnl] entry must be positive in ComputeBacktestLiqPrice().");

			double pct = ComputeBacktestLiqAdversePct (leverage);

			return isLong
				? entry * (1.0 - pct)
				: entry * (1.0 + pct);
			}

		private static (bool hit, double liqExit) CheckLiquidation (
			double entry,
			bool isLong,
			double leverage,
			IReadOnlyList<Candle1m> minutes )
			{
			if (minutes == null || minutes.Count == 0)
				throw new InvalidOperationException ("[pnl] minutes must not be empty in CheckLiquidation().");

			if (entry <= 0.0)
				throw new InvalidOperationException ("[pnl] entry must be positive in CheckLiquidation().");

			double liqPrice = ComputeBacktestLiqPrice (entry, isLong, leverage);

			if (isLong)
				{
				for (int i = 0; i < minutes.Count; i++)
					{
					if (minutes[i].Low <= liqPrice)
						return (true, liqPrice);
					}
				return (false, double.NaN);
				}
			else
				{
				for (int i = 0; i < minutes.Count; i++)
					{
					if (minutes[i].High >= liqPrice)
						return (true, liqPrice);
					}
				return (false, double.NaN);
				}
			}

		private static double CapWorseThanLiquidation (
			double entry,
			bool isLong,
			double leverage,
			double candidateExit,
			out bool cappedToLiq )
			{
			if (entry <= 0.0)
				throw new InvalidOperationException ("[pnl] entry must be positive in CapWorseThanLiquidation().");

			double liq = ComputeBacktestLiqPrice (entry, isLong, leverage);

			// Для long хуже ликвидации — ниже liq; для short хуже — выше liq.
			if (isLong)
				{
				if (candidateExit < liq)
					{
					cappedToLiq = true;
					return liq;
					}
				}
			else
				{
				if (candidateExit > liq)
					{
					cappedToLiq = true;
					return liq;
					}
				}

			cappedToLiq = false;
			return candidateExit;
			}
		}
	}
