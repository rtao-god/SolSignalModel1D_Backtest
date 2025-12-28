using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl
	{
	public static partial class PnlCalculator
		{
		private enum HitKind
			{
			TakeProfit,
			StopLoss
			}

		private static DateTime FindFirstHitUtcOrFail (
			IReadOnlyList<Candle1m> minutes,
			bool isLong,
			HitKind kind,
			double levelPrice )
			{
			if (minutes == null || minutes.Count == 0)
				throw new InvalidOperationException ("[pnl] FindFirstHitUtcOrFail: minutes is empty.");

			if (levelPrice <= 0.0)
				throw new InvalidOperationException ("[pnl] FindFirstHitUtcOrFail: levelPrice must be > 0.");

			for (int i = 0; i < minutes.Count; i++)
				{
				var m = minutes[i];

				bool hit;
				if (kind == HitKind.TakeProfit)
					{
					// TP: long — High >= tp, short — Low <= tp
					hit = isLong ? (m.High >= levelPrice) : (m.Low <= levelPrice);
					}
				else
					{
					// SL: long — Low <= sl, short — High >= sl
					hit = isLong ? (m.Low <= levelPrice) : (m.High >= levelPrice);
					}

				if (hit)
					return m.OpenTimeUtc;
				}

			throw new InvalidOperationException (
				$"[pnl] Expected {kind} hit was not found in minute path. " +
				$"level={levelPrice:0.########}. Delayed layer / DayMinutes mismatch.");
			}

		private static int FindFirstMinuteIndexAtOrAfter ( IReadOnlyList<Candle1m> minutes, DateTime tUtc )
			{
			for (int i = 0; i < minutes.Count; i++)
				{
				if (minutes[i].OpenTimeUtc >= tUtc)
					return i;
				}
			return -1;
			}

		private static (double mae, double mfe) ComputeMaeMfe (
			double entryPrice,
			bool isLong,
			IReadOnlyList<Candle1m> minutes )
			{
			if (minutes == null) throw new ArgumentNullException (nameof (minutes));
			if (minutes.Count == 0) throw new InvalidOperationException ("[pnl] ComputeMaeMfe: minutes is empty.");
			if (entryPrice <= 0.0) throw new InvalidOperationException ("[pnl] ComputeMaeMfe: entryPrice must be > 0.");

			double maxAdverse = 0.0;
			double maxFavorable = 0.0;

			for (int i = 0; i < minutes.Count; i++)
				{
				var m = minutes[i];

				if (m.High <= 0.0 || m.Low <= 0.0)
					continue;

				double adverseMove;
				double favorableMove;

				if (isLong)
					{
					adverseMove = (entryPrice - m.Low) / entryPrice;
					if (adverseMove < 0.0) adverseMove = 0.0;

					favorableMove = (m.High - entryPrice) / entryPrice;
					if (favorableMove < 0.0) favorableMove = 0.0;
					}
				else
					{
					adverseMove = (m.High - entryPrice) / entryPrice;
					if (adverseMove < 0.0) adverseMove = 0.0;

					favorableMove = (entryPrice - m.Low) / entryPrice;
					if (favorableMove < 0.0) favorableMove = 0.0;
					}

				if (adverseMove > maxAdverse) maxAdverse = adverseMove;
				if (favorableMove > maxFavorable) maxFavorable = favorableMove;
				}

			return (maxAdverse, maxFavorable);
			}

		/// <summary>
		/// Поиск дневного TP/SL по 1m-окну с резервным закрытием в dayEndUtc.
		/// Если оба уровня в одной минуте — выбираем худший для трейдера исход (SL),
		/// чтобы не завышать PnL на неоднозначных свечах.
		/// </summary>
		private static (double exitPrice, DateTime exitTimeUtc) TryHitDailyExit (
			double entryPrice,
			bool isLong,
			double tpPct,
			double slPct,
			IReadOnlyList<Candle1m> minutes,
			DateTime dayEndUtc )
			{
			if (minutes == null || minutes.Count == 0)
				throw new ArgumentException ("minutes must not be empty for TryHitDailyExit.", nameof (minutes));

			if (entryPrice <= 0.0)
				throw new ArgumentException ("entryPrice must be positive for TryHitDailyExit.", nameof (entryPrice));

			if (isLong)
				{
				double tp = entryPrice * (1.0 + tpPct);
				double sl = slPct > 1e-9 ? entryPrice * (1.0 - slPct) : double.NaN;

				for (int i = 0; i < minutes.Count; i++)
					{
					var m = minutes[i];

					bool hitTp = m.High >= tp;
					bool hitSl = !double.IsNaN (sl) && m.Low <= sl;

					if (hitTp || hitSl)
						{
						if (hitTp && hitSl) return (sl, m.OpenTimeUtc);
						return (hitSl ? sl : tp, m.OpenTimeUtc);
						}
					}
				}
			else
				{
				double tp = entryPrice * (1.0 - tpPct);
				double sl = slPct > 1e-9 ? entryPrice * (1.0 + slPct) : double.NaN;

				for (int i = 0; i < minutes.Count; i++)
					{
					var m = minutes[i];

					bool hitTp = m.Low <= tp;
					bool hitSl = !double.IsNaN (sl) && m.High >= sl;

					if (hitTp || hitSl)
						{
						if (hitTp && hitSl) return (sl, m.OpenTimeUtc);
						return (hitSl ? sl : tp, m.OpenTimeUtc);
						}
					}
				}

			var last = minutes[minutes.Count - 1];
			return (last.Close, dayEndUtc);
			}
		}
	}
