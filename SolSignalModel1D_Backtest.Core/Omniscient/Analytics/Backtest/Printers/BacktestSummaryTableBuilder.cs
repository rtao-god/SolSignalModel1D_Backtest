using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers
	{
	public static class BacktestSummaryTableBuilder
		{
		public sealed class Row
			{
			public string Policy { get; set; } = string.Empty;
			public string Regime { get; set; } = string.Empty;
			public MarginMode Margin { get; set; }
			public int Days { get; set; }
			public int Trades { get; set; }
			public double TotalPnlUsd { get; set; }
			public double AvgDayUsd { get; set; }
			public double AvgWeekUsd { get; set; }
			public double AvgMonthUsd { get; set; }
			public bool HasMicro { get; set; }
			public bool HasSl { get; set; }
			}

		public static List<Row> Build (
			IReadOnlyList<BacktestRecord> records,
			IReadOnlyList<BacktestPolicyResult> policyResults )
			{
			if (records == null) throw new ArgumentNullException (nameof (records));
			if (policyResults == null) throw new ArgumentNullException (nameof (policyResults));

			var recByDate = records
				.GroupBy (r => r.DateUtc.Date)
				.ToDictionary (g => g.Key, g => g.First ());

			var dict = new Dictionary<(string policy, string regime, MarginMode margin), (HashSet<DateTime> days, int trades, double pnlUsd)> ();

			foreach (var pol in policyResults)
				{
				foreach (var dayGrp in pol.Trades.GroupBy (t => t.DateUtc.Date))
					{
					var day = dayGrp.Key;

					string regime = "unknown";
					if (recByDate.TryGetValue (day, out var r))
						regime = r.RegimeDown ? "down" : "normal";

					double dayPnlUsd = dayGrp.Sum (tr => tr.PositionUsd * tr.NetReturnPct / 100.0);
					int dayTrades = dayGrp.Count ();

					var key = (pol.PolicyName, regime, pol.Margin);
					if (!dict.TryGetValue (key, out var agg))
						{
						agg = (new HashSet<DateTime> (), 0, 0.0);
						}

					agg.days.Add (day);
					agg.trades += dayTrades;
					agg.pnlUsd += dayPnlUsd;

					dict[key] = agg;
					}
				}

			var rows = new List<Row> (dict.Count);

			foreach (var kv in dict)
				{
				var key = kv.Key;
				var agg = kv.Value;

				int daysCount = agg.days.Count;
				double avgDay = daysCount > 0 ? agg.pnlUsd / daysCount : 0.0;

				rows.Add (new Row
					{
					Policy = key.policy,
					Regime = key.regime,
					Margin = key.margin,
					Days = daysCount,
					Trades = agg.trades,
					TotalPnlUsd = agg.pnlUsd,
					AvgDayUsd = avgDay,
					AvgWeekUsd = avgDay * 7.0,
					AvgMonthUsd = avgDay * 30.0,
					HasMicro = true,
					HasSl = true
					});
				}

			return rows
				.OrderBy (r => r.Policy)
				.ThenBy (r => r.Margin)
				.ThenBy (r => r.Regime == "normal" ? 0 : r.Regime == "down" ? 1 : 2)
				.ToList ();
			}
		}
	}
