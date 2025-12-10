using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
	{
	/// <summary>
	/// Частичный класс PnlCalculator: вся математика ликвидации и path-статистика MAE/MFE.
	/// Здесь всё, что связано с MaintenanceMarginRate, ценой ликвидации и защитой от выхода хуже ликвы.
	/// </summary>
	public static partial class PnlCalculator
		{
		// === Биржевая математика ликвидации (упрощенно) ===
		private const double MaintenanceMarginRate = 0.004;

		/// <summary>
		/// Фактор смещения backtest-ликвидации относительно теоретической:
		/// фактическая backtest-цена ликвидации берётся немного ближе к Entry,
		/// чем LiqPrice, чтобы грубо учесть проскальзывание, комиссии, фандинг
		/// и сделать модель слегка консервативной.
		/// Пример: 0.97 = ликва в бэктесте на 3% ближе к Entry, чем теоретическое
		/// расстояние до ликвидации.
		/// </summary>
		private const double BacktestLiqAdverseMultiplier = 0.97;

		/// <summary>
		/// Теоретическая доля "расстояния до ликвидации" в процентах от цены.
		/// IMR = 1/leverage, MMR = MaintenanceMarginRate.
		/// Ликвидация наступает, когда adverseMove >= IMR - MMR.
		/// Это теоретическая distance-to-liq, близкая к биржевой.
		/// </summary>
		private static double ComputeLiqAdversePct ( double leverage )
			{
			if (leverage <= 0.0)
				throw new InvalidOperationException ("[pnl] leverage must be positive in ComputeLiqAdversePct().");

			double liqAdversePct = 1.0 / leverage - MaintenanceMarginRate;
			if (liqAdversePct <= 0.0)
				{
				// На очень больших плечах IMR может быть <= MMR.
				// В таком случае оставляем небольшой запас выше нуля.
				liqAdversePct = 1.0 / leverage * 0.9;
				}
			return liqAdversePct;
			}

		/// <summary>
		/// Distance-to-liq для backtest-ликвидации:
		/// та же логика, что и в ComputeLiqAdversePct, но с небольшим уменьшением
		/// расстояния до ликвидации, чтобы backtest-ликва была чуть ближе к Entry
		/// (учёт проскальзывания, комиссий и фандинга).
		/// </summary>
		private static double ComputeBacktestLiqAdversePct ( double leverage )
			{
			double basePct = ComputeLiqAdversePct (leverage);
			if (basePct <= 0.0)
				throw new InvalidOperationException ("[pnl] base liquidation adverse move must be positive.");

			return basePct * BacktestLiqAdverseMultiplier;
			}

		/// <summary>
		/// Теоретическая цена ликвидации для long/short по entry/leverage/MMR.
		/// Не зависит от конкретного пути цены.
		/// </summary>
		private static double ComputeLiqPrice ( double entry, bool isLong, double leverage )
			{
			if (entry <= 0.0)
				throw new InvalidOperationException ("[pnl] entry must be positive in ComputeLiqPrice().");

			double liqAdversePct = ComputeLiqAdversePct (leverage);
			if (liqAdversePct <= 0.0)
				throw new InvalidOperationException ("[pnl] theoretical liquidation adverse move must be positive.");

			return isLong
				? entry * (1.0 - liqAdversePct)
				: entry * (1.0 + liqAdversePct);
			}

		/// <summary>
		/// Backtest-цена ликвидации: тот же принцип, что и в ComputeLiqPrice,
		/// но с уменьшенным adversePct, чтобы ликвидация в моделировании наступала
		/// немного раньше теоретической.
		/// </summary>
		private static double ComputeBacktestLiqPrice ( double entry, bool isLong, double leverage )
			{
			if (entry <= 0.0)
				throw new InvalidOperationException ("[pnl] entry must be positive in ComputeBacktestLiqPrice().");

			double liqAdversePct = ComputeBacktestLiqAdversePct (leverage);
			if (liqAdversePct <= 0.0)
				throw new InvalidOperationException ("[pnl] backtest liquidation adverse move must be positive.");

			return isLong
				? entry * (1.0 - liqAdversePct)
				: entry * (1.0 + liqAdversePct);
			}

		/// <summary>
		/// Проверка достижения backtest-уровня ликвидации на 1m-пути.
		/// Возвращает флаг «ликвидация была» и цену закрытия по ликвидации.
		/// </summary>
		private static (bool hit, double liqExit) CheckLiquidation (
			double entry, bool isLong, double leverage, List<Candle1m> minutes )
			{
			if (minutes == null || minutes.Count == 0)
				throw new InvalidOperationException ("[pnl] minutes must not be empty in CheckLiquidation().");

			if (entry <= 0.0)
				throw new InvalidOperationException ("[pnl] entry must be positive in CheckLiquidation().");

			double liqPrice = ComputeBacktestLiqPrice (entry, isLong, leverage);
			if (liqPrice <= 0.0)
				throw new InvalidOperationException ("[pnl] backtest liquidation price must be positive in CheckLiquidation().");

			if (isLong)
				{
				foreach (var m in minutes)
					{
					if (m.Low <= liqPrice)
						return (true, liqPrice);
					}
				return (false, 0.0);
				}
			else
				{
				foreach (var m in minutes)
					{
					if (m.High >= liqPrice)
						return (true, liqPrice);
					}
				return (false, 0.0);
				}
			}

		/// <summary>
		/// Защита от выхода хуже, чем backtest-ликвидация.
		/// Если candidateExit даёт большую просадку, чем backtest distance-to-liq,
		/// принудительно возвращаем цену ликвидации.
		/// </summary>
		private static double CapWorseThanLiquidation (
			double entry, bool isLong, double leverage, double candidateExit, out bool cappedToLiq )
			{
			if (entry <= 0.0)
				throw new InvalidOperationException ("[pnl] entry must be positive in CapWorseThanLiquidation().");

			double liqAdversePct = ComputeBacktestLiqAdversePct (leverage);

			// Фактическая величина неблагоприятного движения по candidateExit.
			double adverseFact = isLong
				? (entry - candidateExit) / entry
				: (candidateExit - entry) / entry;

			if (adverseFact >= liqAdversePct + 1e-7)
				{
				cappedToLiq = true;
				return ComputeBacktestLiqPrice (entry, isLong, leverage);
				}

			cappedToLiq = false;
			return candidateExit;
			}

		/// <summary>
		/// MAE/MFE по 1m-пути от Entry до конца сделки.
		/// Возвращает доли (0.05 = 5%).
		/// </summary>
		private static (double mae, double mfe) ComputeMaeMfe (
			double entryPrice,
			bool isLong,
			List<Candle1m> minutes )
			{
			if (entryPrice <= 0.0)
				throw new InvalidOperationException ("[pnl] entryPrice must be positive in ComputeMaeMfe().");

			if (minutes == null || minutes.Count == 0)
				throw new InvalidOperationException ("[pnl] minutes must not be empty in ComputeMaeMfe().");

			double maxAdverse = 0.0;
			double maxFavorable = 0.0;

			foreach (var m in minutes)
				{
				if (isLong)
					{
					// adverse: как глубоко проваливались вниз.
					if (m.Low > 0)
						{
						double adv = (entryPrice - m.Low) / entryPrice;
						if (adv > maxAdverse) maxAdverse = adv;
						}
					// favorable: как высоко уходили вверх.
					if (m.High > 0)
						{
						double fav = (m.High - entryPrice) / entryPrice;
						if (fav > maxFavorable) maxFavorable = fav;
						}
					}
				else
					{
					// Шорт: adverse = рост вверх от entry.
					if (m.High > 0)
						{
						double adv = (m.High - entryPrice) / entryPrice;
						if (adv > maxAdverse) maxAdverse = adv;
						}
					// favorable = падение вниз от entry.
					if (m.Low > 0)
						{
						double fav = (entryPrice - m.Low) / entryPrice;
						if (fav > maxFavorable) maxFavorable = fav;
						}
					}
				}

			if (maxAdverse < 0) maxAdverse = 0.0;
			if (maxFavorable < 0) maxFavorable = 0.0;
			return (maxAdverse, maxFavorable);
			}
		}
	}
