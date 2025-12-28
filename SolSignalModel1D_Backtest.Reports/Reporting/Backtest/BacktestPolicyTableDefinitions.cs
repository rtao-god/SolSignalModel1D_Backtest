using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers;

namespace SolSignalModel1D_Backtest.Reports.Reporting.Backtest
	{
	/// <summary>
	/// Набор таблиц метрик по результатам бэктеста политик.
	/// Здесь мы описываем, какие колонки есть и с каким уровнем детализации.
	/// </summary>
	public static class BacktestPolicyTableDefinitions
		{
		/// <summary>
		/// Таблица "Policies" с уровнем simple/technical.
		/// </summary>
		public static MetricTableDefinition<BacktestPolicyResult> Policies { get; } =
			new MetricTableDefinition<BacktestPolicyResult> (
				tableKey: "backtest_policies",
				title: "Политики бэктеста",
				columns: new List<MetricColumnDefinition<BacktestPolicyResult>>
				{
                    // Имя политики: видно и в Simple, и в Technical.
                    new MetricColumnDefinition<BacktestPolicyResult>(
						key: "policy_name",
						simpleTitle: "Политика",
						technicalTitle: "PolicyName",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => r.PolicyName ?? string.Empty
					),

                    // MarginMode: тоже базовая вещь.
                    new MetricColumnDefinition<BacktestPolicyResult>(
						key: "margin_mode",
						simpleTitle: "Маржа",
						technicalTitle: "MarginMode",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => r.Margin.ToString()
					),

                    // Итоговый PnL.
                    new MetricColumnDefinition<BacktestPolicyResult>(
						key: "total_pnl_pct",
						simpleTitle: "Профит, %",
						technicalTitle: "TotalPnlPct (rel)",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => $"{r.TotalPnlPct * 100.0:0.0}%"
					),

                    // MaxDD — в simple можно трактовать как “просадка, %”.
                    new MetricColumnDefinition<BacktestPolicyResult>(
						key: "max_dd_pct",
						simpleTitle: "Макс. просадка, %",
						technicalTitle: "MaxDrawdownPct (rel)",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => $"{r.MaxDdPct * 100.0:0.0}%"
					),

                    // Флаг ликвидации.
                    new MetricColumnDefinition<BacktestPolicyResult>(
						key: "had_liquidation",
						simpleTitle: "Были ликвидации?",
						technicalTitle: "HadLiquidation",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => r.HadLiquidation ? "Да" : "Нет"
					),

                    // WithdrawnTotal — это уже более технарская штука, но можно показывать в обоих.
                    new MetricColumnDefinition<BacktestPolicyResult>(
						key: "withdrawn_total",
						simpleTitle: "Выведено, USD",
						technicalTitle: "WithdrawnTotal (USD)",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => $"{r.WithdrawnTotal:0}"
					),

                    // TradesCount — уже ближе к технарскому.
                    new MetricColumnDefinition<BacktestPolicyResult>(
						key: "trades_count",
						simpleTitle: "Сделки",
						technicalTitle: "TradesCount",
						minLevel: TableDetailLevel.Simple,
						valueSelector: r => (r.Trades?.Count ?? 0).ToString()
					),

                    // Пример чисто технарской колонки: разбивка сделок по источникам.
                    new MetricColumnDefinition<BacktestPolicyResult>(
						key: "trades_by_source",
						simpleTitle: "—", // в simple вообще не показываем.
                        technicalTitle: "TradesBySource",
						minLevel: TableDetailLevel.Technical,
						valueSelector: r => FormatTradesBySource(r)
					)
				});

		private static string FormatTradesBySource ( BacktestPolicyResult r )
			{
			if (r.TradesBySource == null || r.TradesBySource.Count == 0)
				return string.Empty;

			// Пример формата: "baseline=123, delayedA=45, delayedB=10"
			return string.Join (
				", ",
				r.TradesBySource
					.OrderBy (kv => kv.Key, StringComparer.OrdinalIgnoreCase)
					.Select (kv => $"{kv.Key}={kv.Value}"));
			}
		}
	}
