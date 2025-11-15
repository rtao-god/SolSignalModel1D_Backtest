using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	public static class Windowing
		{
		public static List<Candle6h> FilterNyTrainWindows ( List<Candle6h> all, TimeZoneInfo nyTz )
			{
			var res = new List<Candle6h> ();
			foreach (var c in all)
				{
				var ny = TimeZoneInfo.ConvertTimeFromUtc (c.OpenTimeUtc, nyTz);
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
				bool isDst = nyTz.IsDaylightSavingTime (ny);
				if (isDst)
					{
					if (ny.Hour == 8 || ny.Hour == 14) res.Add (c);
					}
				else
					{
					if (ny.Hour == 7 || ny.Hour == 13) res.Add (c);
					}
				}
			return res.OrderBy (c => c.OpenTimeUtc).ToList ();
			}

		public static List<Candle6h> FilterNyMorningOnly ( List<Candle6h> all, TimeZoneInfo nyTz )
			{
			var res = new List<Candle6h> ();
			foreach (var c in all)
				{
				var ny = TimeZoneInfo.ConvertTimeFromUtc (c.OpenTimeUtc, nyTz);
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
				bool isDst = nyTz.IsDaylightSavingTime (ny);
				if (isDst)
					{
					if (ny.Hour == 8) res.Add (c);
					}
				else
					{
					if (ny.Hour == 7) res.Add (c);
					}
				}
			return res.OrderBy (c => c.OpenTimeUtc).ToList ();
			}

		public static bool IsNyMorning ( DateTime utc, TimeZoneInfo nyTz )
			{
			var ny = TimeZoneInfo.ConvertTimeFromUtc (utc, nyTz);
			if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
			bool isDst = nyTz.IsDaylightSavingTime (ny);
			return isDst ? ny.Hour == 8 : ny.Hour == 7;
			}

		public static List<DataRow> BuildSpacedTest ( List<DataRow> rows, int take, int skip, int blocks )
			{
			var res = new List<DataRow> ();
			int n = rows.Count;
			int end = n;
			for (int b = 0; b < blocks; b++)
				{
				int start = end - take;
				if (start < 0) start = 0;
				var part = rows.Skip (start).Take (end - start).ToList ();
				res.AddRange (part);
				end = start - skip;
				if (end <= 0) break;
				}
			return res.OrderBy (r => r.Date).ToList ();
			}

		public static IEnumerable<List<DataRow>> GroupByBlocks ( List<DataRow> rows, int blockSize )
			{
			var sorted = rows.OrderBy (r => r.Date).ToList ();
			var cur = new List<DataRow> ();
			foreach (var r in sorted)
				{
				cur.Add (r);
				if (cur.Count == blockSize)
					{
					yield return cur;
					cur = new List<DataRow> ();
					}
				}
			if (cur.Count > 0) yield return cur;
			}
		}
	}
