using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers
	{
	public static class PolicyBreakdownPrinter
		{
		public static void PrintSummary ( IReadOnlyList<BacktestPolicyResult> results )
			=> PrintSummary (results, "Policy summary");

		public static void PrintSummary ( IReadOnlyList<BacktestPolicyResult> results, string title )
			{
			ConsoleStyler.WriteHeader (title);

			var t = new TextTable ();
			t.AddHeader (
				"Policy",
				"Margin",
				"Trades",
				"W/L",
				"WinRate",
				"Avg trade %",
				"Long cnt",
				"Short cnt",
				"Long $PnL",
				"Short $PnL",
				"Total $PnL",
				"Withdrawn $",
				"Total %",
				"Max DD %",
				"Liq #",
				"RealLiq #"
			);

			foreach (var r in results
				.OrderBy (x => x.PolicyName)
				.ThenBy (x => x.Margin.ToString ()))
				{
				var trades = r.Trades ?? new List<PnLTrade> ();
				var longs = trades.Where (x => x.IsLong).ToList ();
				var shorts = trades.Where (x => !x.IsLong).ToList ();

				int w = trades.Count (x => x.NetReturnPct > 0.0);
				int l = trades.Count - w;
				string wl = $"{w}/{l}";
				string winRate = trades.Count > 0
					? $"{(double) w / trades.Count * 100.0:0.0}%"
					: "—";
				double avgTradePct = trades.Count > 0
					? trades.Average (x => x.NetReturnPct)
					: 0.0;

				// --- "сырые" PnL по трейдам, только для направления ---
				double longUsdRaw = longs.Sum (x => x.PositionUsd * (x.NetReturnPct / 100.0));
				double shortUsdRaw = shorts.Sum (x => x.PositionUsd * (x.NetReturnPct / 100.0));
				double rawTotalUsd = longUsdRaw + shortUsdRaw;

				// --- wealth-based Totals через снапшоты бакетов ---
				double startCapital = 0.0;
				double equityNow = 0.0;

				if (r.BucketSnapshots != null && r.BucketSnapshots.Count > 0)
					{
					startCapital = r.BucketSnapshots.Sum (b => b.StartCapital);
					equityNow = r.BucketSnapshots.Sum (b => b.EquityNow);
					}

				double withdrawn = r.WithdrawnTotal;
				double wealthNow = equityNow + withdrawn;     // активный капитал + выведенное

				double totalUsdWealth;
				if (startCapital > 0.0)
					{
					totalUsdWealth = wealthNow - startCapital;
					}
				else
					{
					// fallback на старую логику, если вдруг нет снапшотов
					totalUsdWealth = wealthNow - startCapital;
					}

				// --- приводим long/short к wealth-based Total, чтобы суммы сходились ---
				double longUsd = longUsdRaw;
				double shortUsd = shortUsdRaw;

				if (startCapital > 0.0 && Math.Abs (rawTotalUsd) > 1e-9)
					{
					double scale = totalUsdWealth / rawTotalUsd;
					longUsd = longUsdRaw * scale;
					shortUsd = shortUsdRaw * scale;
					}

				// "Ужатые" ликвидации по PnL-движку (позиционные)
				int liqCnt = trades.Count (x => x.IsLiquidated);

				// Реальные ликвидации считаем по отдельному флагу.
				// Для cross-маржи вместо числа выводим "—",
				// так как там любая реальная ликвидация фактически = смерть счёта.
				int realLiqCnt = trades.Count (x => x.IsRealLiquidation);
				string realLiqStr = r.Margin == MarginMode.Isolated
					? realLiqCnt.ToString ()
					: "—";

				var line = new[]
				{
					r.PolicyName,
					r.Margin.ToString(),
					trades.Count.ToString(),
					wl,
					winRate,
					$"{avgTradePct:0.00}%",
					longs.Count.ToString(),
					shorts.Count.ToString(),
					$"{Math.Round(longUsd, 2):0.##}$",
					$"{Math.Round(shortUsd, 2):0.##}$",
					$"{Math.Round(totalUsdWealth, 2):0.##}$",
					$"{Math.Round(withdrawn, 2):0.##}$",
					$"{r.TotalPnlPct:0.00}%",
					$"{r.MaxDdPct:0.00}%",
					liqCnt.ToString(),
					realLiqStr
				};

				var color = r.TotalPnlPct >= 0
					? ConsoleStyler.GoodColor
					: ConsoleStyler.BadColor;

				t.AddColoredRow (color, line);
				}

			t.WriteToConsole ();
			}

		/// <summary>
		/// Месячный «скос» направления (агрегировано по всем политикам).
		/// </summary>
		public static void PrintMonthlySkew ( IReadOnlyList<BacktestPolicyResult> results, int months = 12 )
			{
			var allTrades = results
				.SelectMany (r => r.Trades ?? Enumerable.Empty<PnLTrade> ())
				.ToList ();
			if (allTrades.Count == 0) return;

			var groups = allTrades
				.GroupBy (t => new { Y = t.DateUtc.Year, M = t.DateUtc.Month })
				.OrderBy (g => g.Key.Y).ThenBy (g => g.Key.M)
				.ToList ();

			if (groups.Count == 0) return;

			var last = groups.Skip (Math.Max (0, groups.Count - months)).ToList ();

			var t = new TextTable ();
			t.AddHeader ("Month", "Long cnt", "Short cnt", "Long avg %", "Short avg %");

			foreach (var g in last)
				{
				var longs = g.Where (x => x.IsLong).ToList ();
				var shorts = g.Where (x => !x.IsLong).ToList ();

				double longAvg = longs.Count > 0 ? longs.Average (x => x.NetReturnPct) : 0.0;
				double shortAvg = shorts.Count > 0 ? shorts.Average (x => x.NetReturnPct) : 0.0;

				var monthStr = $"{g.Key.Y}-{g.Key.M:00}";
				var color = longAvg + shortAvg >= 0
					? ConsoleStyler.GoodColor
					: ConsoleStyler.BadColor;

				t.AddColoredRow (
					color,
					monthStr,
					longs.Count.ToString (),
					shorts.Count.ToString (),
					$"{longAvg:0.00}%",
					$"{shortAvg:0.00}%"
				);
				}

			t.WriteToConsole ();
			}
		}
	}
