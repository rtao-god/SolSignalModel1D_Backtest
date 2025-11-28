using System;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Analytics.StrategySimulators
	{
	/// <summary>
	/// Консольный вывод MultiRoundStrategyResult.
	/// Логика печати отделена от математики симулятора.
	/// </summary>
	public static class MultiRoundStrategyPrinter
		{
		public static void Print ( MultiRoundStrategyResult r )
			{
			if (r == null) throw new ArgumentNullException (nameof (r));

			Console.WriteLine ();
			Console.WriteLine ("===== Multi-round RSI strategy backtest =====");

			Console.WriteLine ();
			Console.WriteLine ("-- Capital and risk --");
			Console.WriteLine ($"Start balance              : {r.StartBalanceUsd,12:F2} USD");
			Console.WriteLine ($"End balance                : {r.EndBalanceUsd,12:F2} USD");
			Console.WriteLine ($"Total withdrawn profit     : {r.WithdrawnProfitUsd,12:F2} USD");
			Console.WriteLine ($"Max drawdown               : {r.MaxDrawdownUsd,12:F2} USD ({r.MaxDrawdownPct,6:F2} %)");
			Console.WriteLine (
				$"Stake: start -> min        : {r.StakeStartUsd,12:F2} -> {r.StakeMinUsd,12:F2} USD ({r.StakeMinDrawdownPct,6:F2} %)");

			Console.WriteLine ();
			Console.WriteLine ("-- Trades --");
			Console.WriteLine ($"Trades total               : {r.TradesTotal,5}");
			Console.WriteLine ($"  profitable               : {r.TradesProfitable,5}");
			Console.WriteLine ($"  lossy                    : {r.TradesLossy,5}");

			double winrate = r.TradesTotal > 0
				? (double) r.TradesProfitable / r.TradesTotal * 100.0
				: 0.0;

			Console.WriteLine ($"Winrate                    : {winrate,8:F2} %");

			double netPnl = r.GrossProfitUsd + r.GrossLossUsd;
			Console.WriteLine ($"Total PnL (net)            : {netPnl,12:F2} USD");
			Console.WriteLine ($"  gross profit             : {r.GrossProfitUsd,12:F2} USD");
			Console.WriteLine ($"  gross loss               : {r.GrossLossUsd,12:F2} USD");

			Console.WriteLine ();
			Console.WriteLine ("-- Per day --");
			Console.WriteLine ($"Days total                 : {r.DaysTotal,5}");
			Console.WriteLine ($"Average trades per day     : {r.AvgTradesPerDay,8:F2}");
			Console.WriteLine ($"Max trades in single day   : {r.MaxTradesInSingleDay,5}");
			Console.WriteLine ($"Max losing streak (days)   : {r.MaxLosingStreakDays,5}");

			Console.WriteLine ();
			Console.WriteLine ("-- Exit types --");
			Console.WriteLine ($"Hit TP                     : {r.ExitTpCount,5}");
			Console.WriteLine ($"Hit SL                     : {r.ExitSlCount,5}");
			Console.WriteLine ($"Timed exit                 : {r.ExitTimeCount,5}");

			// Распределение по дням недели.
			if (r.PnlByWeekday.Count > 0)
				{
				Console.WriteLine ();
				Console.WriteLine ("-- PnL by weekday (UTC date) --");
				foreach (DayOfWeek dow in Enum.GetValues (typeof (DayOfWeek)))
					{
					if (!r.PnlByWeekday.TryGetValue (dow, out var b))
						continue;

					Console.WriteLine (
						$"{dow,-10}: days = {b.Days,3}, trades = {b.Trades,5}, PnL = {b.PnlUsd,12:F2}");
					}
				}

			// Распределение по времени входа (час, NY).
			if (r.PnlByEntryHourLocal.Count > 0)
				{
				Console.WriteLine ();
				Console.WriteLine ("-- PnL by entry hour (New York time) --");
				foreach (var kv in r.PnlByEntryHourLocal.OrderBy (k => k.Key))
					{
					var b = kv.Value;
					Console.WriteLine (
						$"Hour {b.HourLocal,2}: trades = {b.Trades,5}, PnL = {b.PnlUsd,12:F2}");
					}
				}

			// ATR-бакеты.
			if (r.PnlByAtrBucket.Count > 0)
				{
				Console.WriteLine ();
				Console.WriteLine ("-- PnL by ATR buckets (daily ATR pct, quartiles) --");
				foreach (var b in r.PnlByAtrBucket)
					{
					Console.WriteLine (
						$"{b.Name,-15}: days = {b.Days,3}, trades = {b.Trades,5}, PnL = {b.PnlUsd,12:F2}, " +
						$"ATR range = [{b.AtrFrom:F4}; {b.AtrTo:F4}]");
					}
				}

			// Хвостовые дни.
			if (r.WorstDays.Count > 0)
				{
				Console.WriteLine ();
				Console.WriteLine ("-- Tail: worst days (about 5 percent) --");
				foreach (var d in r.WorstDays.OrderBy (d => d.DayPnlUsd))
					{
					Console.WriteLine (
						$"{d.DateUtc:yyyy-MM-dd}: trades = {d.Trades,3}, PnL = {d.DayPnlUsd,12:F2}, ATR = {d.AtrPct:F4}");
					}
				}

			if (r.BestDays.Count > 0)
				{
				Console.WriteLine ();
				Console.WriteLine ("-- Tail: best days (about 5 percent) --");
				foreach (var d in r.BestDays.OrderByDescending (d => d.DayPnlUsd))
					{
					Console.WriteLine (
						$"{d.DateUtc:yyyy-MM-dd}: trades = {d.Trades,3}, PnL = {d.DayPnlUsd,12:F2}, ATR = {d.AtrPct:F4}");
					}
				}

			// Equity-curve.
			if (r.EquityCurve.Count > 0)
				{
				Console.WriteLine ();
				Console.WriteLine ("-- Equity curve by day --");
				foreach (var p in r.EquityCurve.OrderBy (p => p.DateUtc))
					{
					Console.WriteLine (
						$"{p.DateUtc:yyyy-MM-dd}: equity = {p.EquityUsd,12:F2} USD");
					}
				}

			Console.WriteLine ();
			}
		}
	}
