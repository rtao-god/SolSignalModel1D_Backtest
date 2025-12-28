using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Causal.Utils
	{
	public static class BacktestHelpers
		{
		/// <summary>
		/// На вход — время утреннего окна (6h-свечи), на выход:
		/// цена входа, максимум high за 24ч, минимум low за 24ч и close через 24ч
		/// </summary>
		public static (double entry, double maxHigh, double minLow, double fwdClose) GetForwardInfo (
			DateTime openUtc,
			IDictionary<DateTime, Candle6h> sol6hDict )
			{
			if (!sol6hDict.TryGetValue (openUtc, out var cur))
				return (0, 0, 0, 0);

			double entry = cur.Open;
			double maxHigh = cur.High;
			double minLow = cur.Low;
			double fwdClose = cur.Close;

			// 4 свечи по 6h = 24 часа
			for (int i = 1; i <= 4; i++)
				{
				var t = openUtc.AddHours (6 * i);
				if (!sol6hDict.TryGetValue (t, out var nxt))
					break;

				if (nxt.High > maxHigh) maxHigh = nxt.High;
				if (nxt.Low < minLow) minLow = nxt.Low;
				fwdClose = nxt.Close;
				}

			return (entry, maxHigh, minLow, fwdClose);
			}
		}
	}
