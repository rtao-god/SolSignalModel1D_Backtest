using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Data.Time
	{
	/// <summary>
	/// Фильтры свечей для выделения NY-окон.
	/// </summary>
	public static class NyCandleWindowFilters
		{
		public static List<Candle6h> FilterNyTrainWindows ( List<Candle6h> all, TimeZoneInfo nyTz )
			{
			if (all == null) throw new ArgumentNullException (nameof (all));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			var res = new List<Candle6h> ();

			foreach (var c in all)
				{
				var ny = TimeZoneInfo.ConvertTimeFromUtc (c.OpenTimeUtc, nyTz);
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					continue;

				bool isDst = nyTz.IsDaylightSavingTime (ny);

				if (isDst)
					{
					if (ny.Hour == 8 || ny.Hour == 14)
						res.Add (c);
					}
				else
					{
					if (ny.Hour == 7 || ny.Hour == 13)
						res.Add (c);
					}
				}

			return res.OrderBy (c => c.OpenTimeUtc).ToList ();
			}

		public static List<Candle6h> FilterNyMorningOnly ( List<Candle6h> all, TimeZoneInfo nyTz )
			{
			if (all == null) throw new ArgumentNullException (nameof (all));
			if (nyTz == null) throw new ArgumentNullException (nameof (nyTz));

			var res = new List<Candle6h> ();

			foreach (var c in all)
				{
				var ny = TimeZoneInfo.ConvertTimeFromUtc (c.OpenTimeUtc, nyTz);
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					continue;

				bool isDst = nyTz.IsDaylightSavingTime (ny);

				if (isDst)
					{
					if (ny.Hour == 8)
						res.Add (c);
					}
				else
					{
					if (ny.Hour == 7)
						res.Add (c);
					}
				}

			return res.OrderBy (c => c.OpenTimeUtc).ToList ();
			}
		}
	}
