using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Causal.Domain
	{
	/// <summary>
	/// Авто-фибо по свечам: три окна (30/90/180 дней), уровни вниз от хая, возвращаем ближайший вверх/вниз.
	/// </summary>
	public static class FiboLevels
		{
		private static readonly double[] Ratios = new[]
		{
			0.236,
			0.382,
			0.5,
			0.618,
			0.786
		};

		public static (double up, double down) GetNearest ( List<Candle6h> all, DateTime asOfUtc, double currentPrice )
			{
			if (currentPrice <= 0 || all.Count == 0)
				return (0.0, 0.0);

			double upBest = double.PositiveInfinity;
			double downBest = double.PositiveInfinity;

			EvalWindow (all, asOfUtc, currentPrice, 30, ref upBest, ref downBest);
			EvalWindow (all, asOfUtc, currentPrice, 90, ref upBest, ref downBest);
			EvalWindow (all, asOfUtc, currentPrice, 180, ref upBest, ref downBest);

			if (double.IsPositiveInfinity (upBest)) upBest = 0.0;
			if (double.IsPositiveInfinity (downBest)) downBest = 0.0;

			return (upBest, downBest);
			}

		private static void EvalWindow ( List<Candle6h> all, DateTime asOfUtc, double currentPrice, int daysBack,
			ref double upBest, ref double downBest )
			{
			DateTime from = asOfUtc.AddDays (-daysBack);
			var seg = all.Where (c => c.OpenTimeUtc >= from && c.OpenTimeUtc <= asOfUtc).ToList ();
			if (seg.Count < 10) return;

			double hi = seg.Max (c => c.High);
			double lo = seg.Min (c => c.Low);
			if (hi <= 0 || lo <= 0 || hi <= lo) return;

			foreach (var r in Ratios)
				{
				double lvl = hi - (hi - lo) * r;
				double rel = Math.Abs (lvl - currentPrice) / currentPrice;
				if (lvl >= currentPrice)
					{
					if (rel < upBest) upBest = rel;
					}
				else
					{
					if (rel < downBest) downBest = rel;
					}
				}
			}
		}
	}
