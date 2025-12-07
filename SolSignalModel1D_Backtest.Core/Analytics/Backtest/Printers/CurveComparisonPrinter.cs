using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Format;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.Printers
	{
	public static class CurveComparisonPrinter
		{
		public static void Print (
			IReadOnlyList<PnLTrade> trades,
			SortedDictionary<DateTime, double> combatEq,
			double startEquity,
			double maxDdCombatPct )
			{
			Console.WriteLine ();
			ConsoleStyler.WithColor (ConsoleStyler.HeaderColor, () =>
			{
				Console.WriteLine ("=== Curves sharpe/comparisons ===");
			});

			var (combatSharpe, combatSortino) = BacktestSeriesUtils.ComputeSharpeSortino (combatEq);
			double combatCalmar = maxDdCombatPct > 0.001
				? (combatEq.Last ().Value - startEquity) / startEquity / (maxDdCombatPct / 100.0)
				: 0.0;

			// daily-only
			var dailyTrades = trades.Where (t => t.Source == "Daily").OrderBy (t => t.DateUtc).ToList ();
			var dailyEq = BacktestSeriesUtils.BuildDailyEquity (dailyTrades, startEquity);
			var (dailySharpe, dailySortino) = BacktestSeriesUtils.ComputeSharpeSortino (dailyEq);
			double dailyMaxDd = BacktestSeriesUtils.ComputeMaxDrawdownFromCurve (dailyEq);
			double dailyFinalEq = dailyTrades.Count > 0 ? dailyTrades.Last ().EquityAfter : startEquity;
			double dailyCalmar = dailyMaxDd > 0.001
				? (dailyFinalEq - startEquity) / startEquity / (dailyMaxDd / 100.0)
				: 0.0;

			var t = new TextTable ();
			t.AddHeader ("curve", "Sharpe", "Sortino", "Calmar");
			t.AddRow (
				"Combat (cross)",
				ConsoleNumberFormatter.RatioShort (combatSharpe),
				ConsoleNumberFormatter.RatioShort (combatSortino),
				ConsoleNumberFormatter.RatioShort (combatCalmar)
			);
			t.AddRow (
				"Daily-only",
				ConsoleNumberFormatter.RatioShort (dailySharpe),
				ConsoleNumberFormatter.RatioShort (dailySortino),
				ConsoleNumberFormatter.RatioShort (dailyCalmar)
			);
			t.WriteToConsole ();
			}
		}
	}
