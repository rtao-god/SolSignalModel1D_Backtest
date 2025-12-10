using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
	{
	/// <summary>
	/// Частичный класс PnlCalculator: логика дневного TP/SL в baseline-окне.
	/// </summary>
	public static partial class PnlCalculator
		{
		/// <summary>
		/// Поиск дневного TP/SL внутри 1m-окна [entry; ...),
		/// с резервным закрытием в defaultExitUtc (baseline-выход).
		/// </summary>
		private static (double exitPrice, DateTime exitTime) TryHitDailyExit (
			double entry,
			bool isLong,
			double tpPct,
			double slPct,
			List<Candle1m> dayMinutes,
			DateTime defaultExitUtc )
			{
			if (dayMinutes == null || dayMinutes.Count == 0)
				throw new ArgumentException ("dayMinutes must not be empty for TryHitDailyExit.", nameof (dayMinutes));

			if (entry <= 0.0)
				throw new ArgumentException ("entry must be positive for TryHitDailyExit.", nameof (entry));

			if (isLong)
				{
				double tp = entry * (1.0 + tpPct);
				double sl = slPct > 1e-9 ? entry * (1.0 - slPct) : double.NaN;

				foreach (var m in dayMinutes)
					{
					bool hitTp = m.High >= tp;
					bool hitSl = !double.IsNaN (sl) && m.Low <= sl;
					if (hitTp || hitSl)
						{
						return (hitSl ? sl : tp, m.OpenTimeUtc);
						}
					}
				}
			else
				{
				double tp = entry * (1.0 - tpPct);
				double sl = slPct > 1e-9 ? entry * (1.0 + slPct) : double.NaN;

				foreach (var m in dayMinutes)
					{
					bool hitTp = m.Low <= tp;
					bool hitSl = !double.IsNaN (sl) && m.High >= sl;
					if (hitTp || hitSl)
						{
						return (hitSl ? sl : tp, m.OpenTimeUtc);
						}
					}
				}

			// Ни TP, ни SL — закрываемся по close последней минутки,
			// но временем выхода считаем строго defaultExitUtc (baseline-выход).
			var last = dayMinutes.Last ();
			return (last.Close, defaultExitUtc);
			}
		}
	}
