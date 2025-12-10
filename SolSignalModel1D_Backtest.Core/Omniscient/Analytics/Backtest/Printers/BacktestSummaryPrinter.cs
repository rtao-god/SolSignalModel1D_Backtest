using SolSignalModel1D_Backtest.Core.Backtest;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Консольная сводка по BacktestSummary:
	/// - окно дат, общее количество сигналов и трейдов;
	/// - базовые метрики (лучший PnL, худший max DD, ликвидации);
	/// - baseline-конфиг;
	/// - плоская таблица по политикам (BASE/ANTI-D × SL/NO SL).
	/// Логика расчёта уже заложена в BacktestSummary/BacktestEngine,
	/// здесь только форматирование.
	/// </summary>
	public static class BacktestSummaryPrinter
		{
		public static void Print ( BacktestSummary summary )
			{
			if (summary == null)
				throw new ArgumentNullException (nameof (summary));

			Console.WriteLine ();
			Console.WriteLine ("===== BACKTEST SUMMARY =====");
			Console.WriteLine (
				$"Window: {summary.FromDateUtc:yyyy-MM-dd} .. {summary.ToDateUtc:yyyy-MM-dd}");
			Console.WriteLine ($"Signal days:           {summary.SignalDays}");
			Console.WriteLine ($"BestTotalPnlPct:       {summary.BestTotalPnlPct:0.00} %");
			Console.WriteLine ($"WorstMaxDdPct:         {summary.WorstMaxDdPct:0.00} %");
			Console.WriteLine ($"PoliciesWithLiquidation: {summary.PoliciesWithLiquidation}");
			Console.WriteLine ($"TotalTrades:           {summary.TotalTrades}");
			Console.WriteLine ();

			var cfg = summary.Config;
			if (cfg != null)
				{
				Console.WriteLine ("-- Config (baseline) --");
				Console.WriteLine ($"DailyStopPct: {cfg.DailyStopPct * 100.0:0.0} %");
				Console.WriteLine ($"DailyTpPct:   {cfg.DailyTpPct * 100.0:0.0} %");
				Console.WriteLine ();

				if (cfg.Policies != null && cfg.Policies.Count > 0)
					{
					Console.WriteLine ("Name                Type         Leverage  Margin");
					foreach (var p in cfg.Policies)
						{
						var lev = p.Leverage.HasValue
							? p.Leverage.Value.ToString ("0.##")
							: "-";

						Console.WriteLine (
							"{0,-18} {1,-10} {2,8}  {3}",
							Truncate (p.Name, 18),
							Truncate (p.PolicyType, 10),
							lev,
							p.MarginMode);
						}

					Console.WriteLine ();
					}
				}

			Console.WriteLine ("-- Policies (BASE/ANTI-D × SL/NO SL) --");
			Console.WriteLine ("Policy              Margin   Branch   SL-mode    Total%    MaxDD%  Trades  Liq  Withdrawn");

			void Dump ( IEnumerable<BacktestPolicyResult>? src, string branch, bool withSl )
				{
				if (src == null) return;

				var slLabel = withSl ? "WITH_SL" : "NO_SL";

				foreach (var r in src)
					{
					var tradesCount = r.Trades?.Count ?? 0;

					Console.WriteLine (
						"{0,-18} {1,-7} {2,-7} {3,-9} {4,8:0.00} {5,8:0.00} {6,6} {7,4} {8,10:0.00}",
						Truncate (r.PolicyName, 18),
						r.Margin,
						branch,
						slLabel,
						r.TotalPnlPct,
						r.MaxDdPct,
						tradesCount,
						r.HadLiquidation ? "yes" : "no",
						r.WithdrawnTotal);
					}
				}

			Dump (summary.WithSlBase, "BASE", true);
			Dump (summary.NoSlBase, "BASE", false);
			Dump (summary.WithSlAnti, "ANTI-D", true);
			Dump (summary.NoSlAnti, "ANTI-D", false);

			Console.WriteLine ();
			}

		/// <summary>
		/// Усечение строк для компактного консольного вывода.
		/// </summary>
		private static string Truncate ( string value, int maxLen )
			{
			if (string.IsNullOrEmpty (value) || value.Length <= maxLen)
				return value;

			return value.Substring (0, maxLen - 1) + "…";
			}
		}
	}
