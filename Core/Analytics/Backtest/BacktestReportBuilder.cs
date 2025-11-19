using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	/// <summary>
	/// Обёртки-репорты: только дергают табличные принтеры.
	/// Без “полотна” текста.
	/// </summary>
	public static class BacktestReportBuilder
		{
		/// <summary>
		/// Базовый набор таблиц по политикам:
		/// - сводная таблица;
		/// - топ/анти-топ трейды;
		/// - месячный скос лонг/шорт;
		/// - разрез по источникам сигналов.
		/// </summary>
		public static void PrintPolicies (
			IReadOnlyList<PredictionRecord> allRecords,
			IReadOnlyList<BacktestPolicyResult> results )
			{
			if (allRecords == null) throw new ArgumentNullException (nameof (allRecords));
			if (results == null) throw new ArgumentNullException (nameof (results));

			// 1) сводная таблица по политикам
			PolicyBreakdownPrinter.PrintSummary (results);

			// 2) “топ-1/анти-топ-1” по каждой политике (мини-таблица)
			var allTrades = results
				.SelectMany (r => r.Trades ?? Enumerable.Empty<PnLTrade> ())
				.ToList ();
			TopTradesPrinter.PrintTop1PerPolicy (allTrades);

			// 3) скос “лонг/шорт” по месяцам (таблица)
			PolicyBreakdownPrinter.PrintMonthlySkew (results, months: 12);

			// 4) разрез по источникам сигналов (таблица)
			SourceBreakdownPrinter.Print (allTrades, MergeSources (results), startEquity: 20000.0);

			// ВАЖНО:
			// Сравнение SL vs No-SL и Delayed A/B вынесено в отдельные методы ниже.
			}

		/// <summary>
		/// Отдельная точка входа для сравнения SL vs No-SL.
		/// Её должен вызывать код, который уже посчитал два набора BacktestPolicyResult.
		/// </summary>
		public static void PrintSlComparison (
			IReadOnlyList<BacktestPolicyResult> withSlResults,
			IReadOnlyList<BacktestPolicyResult> noSlResults )
			{
			if (withSlResults == null) throw new ArgumentNullException (nameof (withSlResults));
			if (noSlResults == null) throw new ArgumentNullException (nameof (noSlResults));

			// 1) наша большая таблица WITH SL vs WITHOUT SL
			//PolicySlComparisonPrinter.Print (withSlResults, noSlResults);

			// 2) Топ/анти-топ сделки по политикам, объединённым из обоих наборов
			PolicyTopTradesPrinter.PrintTop1 (
				withSlResults.Concat (noSlResults),
				"Top 1 best / worst per policy (WITH vs WITHOUT SL)"
			);
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
