using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	/// <summary>
	/// Обёртки-репорты: только дергают табличные принтеры.
	/// Без “полотна” текста.
	/// </summary>
	public static class BacktestReportBuilder
		{
		public static void PrintPolicies (
			IReadOnlyList<PredictionRecord> allRecords,
			IReadOnlyList<BacktestPolicyResult> results )
			{
			// 1) сводная таблица по политикам
			PolicyBreakdownPrinter.PrintSummary (results);

			// 2) “топ-1/анти-топ-1” по каждой политике (мини-таблица)
			var allTrades = results.SelectMany (r => r.Trades ?? Enumerable.Empty<PnLTrade> ()).ToList ();
			TopTradesPrinter.PrintTop1PerPolicy (allTrades);

			// 3) скос “лонг/шорт” по месяцам (таблица)
			PolicyBreakdownPrinter.PrintMonthlySkew (results, months: 12);

			// 4) разрез по источникам сигналов (таблица)
			SourceBreakdownPrinter.Print (allTrades, MergeSources (results), startEquity: 20000.0);
			}

		private static IReadOnlyDictionary<string, int> MergeSources ( IReadOnlyList<BacktestPolicyResult> results )
			{
			var dict = new Dictionary<string, int> (StringComparer.OrdinalIgnoreCase);
			foreach (var r in results)
				{
				if (r.TradesBySource == null) continue;
				foreach (var kv in r.TradesBySource)
					{
					dict.TryGetValue (kv.Key, out var cur);
					dict[kv.Key] = cur + kv.Value;
					}
				}
			return dict;
			}
		}
	}
