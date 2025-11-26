using SolSignalModel1D_Backtest.Core.Analytics.Backtest;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Reports.Model;

namespace SolSignalModel1D_Backtest.Reports.Backtest.Reports
	{
	/// <summary>
	/// Строит ReportDocument для сводки по бэктесту (backtest_summary)
	/// на основе BacktestSummary (модельный результат).
	/// Никакой новой математики не добавляет — только форматирует данные
	/// для фронта в виде key-value и таблиц.
	/// </summary>
	public static class BacktestSummaryReportBuilder
		{
		/// <summary>
		/// Строит DTO-репорт по результатам бэктеста.
		/// На вход подаётся готовый BacktestSummary из BacktestEngine.
		/// </summary>
		public static ReportDocument? Build ( BacktestSummary summary )
			{
			if (summary == null) throw new ArgumentNullException (nameof (summary));

			// Если по какой-то причине нет политик — смысла в отчёте нет.
			bool hasAny =
				(summary.WithSlBase?.Count ?? 0) > 0 ||
				(summary.NoSlBase?.Count ?? 0) > 0 ||
				(summary.WithSlAnti?.Count ?? 0) > 0 ||
				(summary.NoSlAnti?.Count ?? 0) > 0;

			if (!hasAny)
				return null;

			var cfg = summary.Config;

			// 1) Шапка ReportDocument.
			var doc = new ReportDocument
				{
				Id = $"backtest-summary-{summary.ToDateUtc:yyyyMMdd}",
				Kind = "backtest_summary",
				Title = "Сводка бэктеста (SOLUSDT)",
				GeneratedAtUtc = DateTime.UtcNow
				};

			// === Общие параметры окна бэктеста ===
			var summarySection = new KeyValueSection
				{
				Title = "Общие параметры бэктеста"
				};

			summarySection.Items.Add (new KeyValueItem
				{
				Key = "FromDateUtc",
				Value = summary.FromDateUtc.ToString ("O")
				});

			summarySection.Items.Add (new KeyValueItem
				{
				Key = "ToDateUtc",
				Value = summary.ToDateUtc.ToString ("O")
				});

			summarySection.Items.Add (new KeyValueItem
				{
				Key = "SignalDays",
				Value = summary.SignalDays.ToString ()
				});

			int policyCount = cfg.Policies?.Count ?? 0;
			summarySection.Items.Add (new KeyValueItem
				{
				Key = "PolicyCount",
				Value = policyCount.ToString ()
				});

			summarySection.Items.Add (new KeyValueItem
				{
				Key = "BestTotalPnlPct",
				Value = summary.BestTotalPnlPct.ToString ("0.00")
				});

			summarySection.Items.Add (new KeyValueItem
				{
				Key = "WorstMaxDdPct",
				Value = summary.WorstMaxDdPct.ToString ("0.00")
				});

			summarySection.Items.Add (new KeyValueItem
				{
				Key = "PoliciesWithLiquidation",
				Value = summary.PoliciesWithLiquidation.ToString ()
				});

			summarySection.Items.Add (new KeyValueItem
				{
				Key = "TotalTrades",
				Value = summary.TotalTrades.ToString ()
				});

			doc.KeyValueSections.Add (summarySection);

			// === Секция с baseline-конфигом (SL/TP + агрегированная информация) ===
			var cfgSection = new KeyValueSection
				{
				Title = "Backtest config (baseline)"
				};

			cfgSection.Items.Add (new KeyValueItem
				{
				Key = "DailyStopPct",
				Value = (cfg.DailyStopPct * 100.0).ToString ("0.0") + "%"
				});

			cfgSection.Items.Add (new KeyValueItem
				{
				Key = "DailyTpPct",
				Value = (cfg.DailyTpPct * 100.0).ToString ("0.0") + "%"
				});

			doc.KeyValueSections.Add (cfgSection);

			// Таблица с baseline-политиками.
			if (cfg.Policies != null && cfg.Policies.Count > 0)
				{
				var cfgPoliciesTable = new TableSection
					{
					Title = "Policies (baseline config)"
					};

				cfgPoliciesTable.Columns.AddRange (new[]
				{
					"Name",
					"Type",
					"Leverage",
					"MarginMode"
				});

				foreach (var p in cfg.Policies)
					{
					cfgPoliciesTable.Rows.Add (new List<string>
						{
						p.Name,
						p.PolicyType,
						p.Leverage.HasValue ? p.Leverage.Value.ToString ("0.##") : "-",
						p.MarginMode.ToString ()
						});
					}

				doc.TableSections.Add (cfgPoliciesTable);
				}

			// === Таблица по политикам: BASE / ANTI-D × SL / NO SL ===
			var table = BuildPoliciesTable (
				summary.WithSlBase,
				summary.NoSlBase,
				summary.WithSlAnti,
				summary.NoSlAnti
			);

			doc.TableSections.Add (table);

			return doc;
			}

		private static TableSection BuildPoliciesTable (
			IReadOnlyList<BacktestPolicyResult>? withSlBase,
			IReadOnlyList<BacktestPolicyResult>? noSlBase,
			IReadOnlyList<BacktestPolicyResult>? withSlAnti,
			IReadOnlyList<BacktestPolicyResult>? noSlAnti )
			{
			var table = new TableSection
				{
				Title = "Политики бэктеста (BASE/ANTI-D × SL/NO SL)"
				};

			table.Columns.AddRange (new[]
			{
				"Policy",
				"Margin",
				"Branch",
				"StopLoss",
				"TotalPnlPct",
				"MaxDdPct",
				"Trades",
				"WithdrawnTotal",
				"HadLiquidation",
				"TradesBySource"
			});

			void AppendRows (
				IEnumerable<BacktestPolicyResult> source,
				string branch,
				bool stopLoss )
				{
				var stopLossLabel = stopLoss ? "WITH_SL" : "NO_SL";

				foreach (var r in source)
					{
					var tradesCount = r.Trades?.Count ?? 0;

					var tradesBySource = (r.TradesBySource != null && r.TradesBySource.Count > 0)
						? string.Join (", ", r.TradesBySource.Select (kv => $"{kv.Key}={kv.Value}"))
						: "-";

					table.Rows.Add (new List<string>
						{
						r.PolicyName,
						r.Margin.ToString (),
						branch,
						stopLossLabel,
						r.TotalPnlPct.ToString ("0.00"),
						r.MaxDdPct.ToString ("0.00"),
						tradesCount.ToString (),
						r.WithdrawnTotal.ToString ("0.00"),
						r.HadLiquidation.ToString (),
						tradesBySource
						});
					}
				}

			AppendRows (withSlBase ?? Array.Empty<BacktestPolicyResult> (), "BASE", true);
			AppendRows (noSlBase ?? Array.Empty<BacktestPolicyResult> (), "BASE", false);
			AppendRows (withSlAnti ?? Array.Empty<BacktestPolicyResult> (), "ANTI-D", true);
			AppendRows (noSlAnti ?? Array.Empty<BacktestPolicyResult> (), "ANTI-D", false);

			return table;
			}
		}
	}
