using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Utils.Backtest;
using SolSignalModel1D_Backtest.Core.Causal.Utils;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Сравнение политик BASE vs ANTI-Direction.
	/// Для каждого режима (BASE и ANTI-D overlay) печатает ТРИ таблицы:
	///   1) PnL-таблица (Total %, Total $, Withdrawn, OnExch и т.д.).
	///   2) Risk-таблица (ликвидации, BalMin, время ниже порога, восстановление из MaxDD).
	///   3) Доходности по горизонту (день/неделя/месяц/год) на основе wealth-кривой.
	/// Формат строк и логика — как в PolicySlComparisonPrinter:
	/// внутри режима строки идут: "with SL", затем "no SL", затем снова "with SL"/"no SL" по политикам.
	/// </summary>
	public static class AntiDirectionComparisonPrinter
		{
		/// <param name="withSlBase">BASE-режим, WITH SL.</param>
		/// <param name="withSlAnti">ANTI-D-режим, WITH SL.</param>
		/// <param name="noSlBase">BASE-режим, NO SL.</param>
		/// <param name="noSlAnti">ANTI-D-режим, NO SL.</param>
		public static void Print (
			IReadOnlyList<BacktestPolicyResult> withSlBase,
			IReadOnlyList<BacktestPolicyResult> withSlAnti,
			IReadOnlyList<BacktestPolicyResult> noSlBase,
			IReadOnlyList<BacktestPolicyResult> noSlAnti )
			{
			// Блок 1: BASE direction
			PrintModeBlock (
				modeLabel: "BASE direction",
				withSl: withSlBase,
				noSl: noSlBase
			);

			// Блок 2: ANTI-D overlay
			PrintModeBlock (
				modeLabel: "ANTI-D overlay",
				withSl: withSlAnti,
				noSl: noSlAnti
			);
			}

		/// <summary>
		/// Печатает три таблицы для одного режима (BASE или ANTI-D):
		/// PnL, Risk/Drawdown, Returns-by-horizon.
		/// </summary>
		private static void PrintModeBlock (
			string modeLabel,
			IReadOnlyList<BacktestPolicyResult> withSl,
			IReadOnlyList<BacktestPolicyResult> noSl )
			{
			if (withSl == null || withSl.Count == 0)
				return;

			var rows = BuildRows (withSl, noSl);
			if (rows.Count == 0)
				return;

			// 1) PnL-таблица
			PrintPnlTable (
				title: $"=== Policies ({modeLabel}): WITH SL vs WITHOUT SL (PnL) ===",
				rows: rows
			);

			// 2) Таблица риска / ликвидаций
			PrintRiskTable (
				title: $"=== Policies ({modeLabel}): Risk / Drawdown metrics ===",
				rows: rows
			);

			// 3) Таблица доходностей по горизонту
			PrintReturnHorizonTable (
				title: $"=== Policies ({modeLabel}): Avg returns by horizon (calendar) ===",
				rows: rows
			);
			}

		/// <summary>
		/// Собирает PolicyRowMetrics для WITH SL и соответствующих NO SL.
		/// Порядок: для каждой политики — сначала with SL, потом no SL (если есть).
		/// </summary>
		private static List<PolicySlMetrics.PolicyRowMetrics> BuildRows (
			IReadOnlyList<BacktestPolicyResult> withSl,
			IReadOnlyList<BacktestPolicyResult> noSl )
			{
			var rows = new List<PolicySlMetrics.PolicyRowMetrics> ();

			foreach (var w in withSl
				.OrderBy (x => x.PolicyName)
				.ThenBy (x => x.Margin.ToString ()))
				{
				// WITH SL
				rows.Add (PolicySlMetrics.BuildMetrics (w, "with SL"));

				// Пара NO SL, если есть
				var n = noSl?
					.FirstOrDefault (x => x.PolicyName == w.PolicyName && x.Margin.Equals (w.Margin));

				if (n != null)
					{
					rows.Add (PolicySlMetrics.BuildMetrics (n, "no SL"));
					}
				}

			return rows;
			}

		/// <summary>
		/// PnL-таблица:
		/// Policy / Margin / Mode / Trades / Total% / Total$ / MaxDD% / Withdrawn / OnExch$ /
		/// Long/Short n/$ и средние проценты.
		/// Формат полностью совпадает с PolicySlComparisonPrinter.PrintPnlTable.
		/// </summary>
		private static void PrintPnlTable (
			string title,
			IReadOnlyList<PolicySlMetrics.PolicyRowMetrics> rows )
			{
			ConsoleStyler.WriteHeader (title);

			var t = new TextTable ();
			t.AddHeader (
				"Policy",
				"Margin",
				"Mode",
				"Trades",
				"Total %",
				"Total $",
				"Max DD %",
				"Withdrawn",
				"OnExch $",
				"Long n",
				"Short n",
				"Long $",
				"Short $",
				"Avg Long %",
				"Avg Short %"
			);

			foreach (var m in rows)
				{
				string maxDdStr = $"{m.MaxDdFrac * 100.0:0.00}%";

				var line = new[]
				{
					m.PolicyName,
					m.Margin.ToString(),
					m.Mode,
					m.TradesCount.ToString(),
					$"{m.TotalPct:0.00}%",
					$"{Math.Round(m.TotalUsd, 2):0.##}$",
					maxDdStr,
					$"{Math.Round(m.WithdrawnUsd, 2):0.##}$",
					$"{Math.Round(m.OnExchUsd, 2):0.##}$",
					m.LongCount.ToString(),
					m.ShortCount.ToString(),
					$"{Math.Round(m.LongUsd, 2):0.##}$",
					$"{Math.Round(m.ShortUsd, 2):0.##}$",
					$"{m.AvgLongPct:0.00}%",
					$"{m.AvgShortPct:0.00}%"
				};

				var color = ChooseRowColorBySurvival (m);
				t.AddColoredRow (color, line);
				}

			t.WriteToConsole ();
			}

		/// <summary>
		/// Risk-таблица: ликвидации, баланс, восстановление из MaxDD, время «внизу», ReqGain%.
		/// Формат совпадает с PolicySlComparisonPrinter.PrintRiskTable.
		/// </summary>
		private static void PrintRiskTable (
			string title,
			IReadOnlyList<PolicySlMetrics.PolicyRowMetrics> rows )
			{
			ConsoleStyler.WriteHeader (title);

			var t = new TextTable ();
			t.AddHeader (
				"Policy",
				"Margin",
				"Mode",
				"PosLiq #",
				"RealLiq #",
				"AccRuin",
				"BalMin %",
				"Bal<35%",
				"Recovered?",
				"RecovDays (cal)",
				"RecovDays (signal)",
				"Time<35% (cal)",
				"ReqGain% @Min"
			);

			foreach (var m in rows)
				{
				string balMinStr = m.StartCapital > 0.0
					? $"{m.BalMinFrac * 100.0:0.0}%"
					: "n/a";

				string balDeathStr = m.BalDead ? "YES" : "no";
				string recoveredStr = m.Recovered ? "YES" : "no";

				string recovDaysCalStr =
					m.Recovered && m.RecovDaysCal >= 0.0
						? $"{m.RecovDaysCal:0.0}"
						: "—";

				string recovSignalsStr =
					m.Recovered && m.RecovSignals >= 0
						? m.RecovSignals.ToString ()
						: "—";

				string timeBelowStr =
					m.TimeBelowThreshDays > 0.0
						? $"{m.TimeBelowThreshDays:0.0}"
						: "0.0";

				string reqGainStr =
					m.ReqGainPct > 0.0 && double.IsFinite (m.ReqGainPct)
						? $"{m.ReqGainPct:0.0}%"
						: double.IsPositiveInfinity (m.ReqGainPct) ? "INF" : "0.0%";

				string realLiqStr = m.Margin == MarginMode.Isolated
					? m.RealLiqCount.ToString ()
					: "—";

				var line = new[]
				{
					m.PolicyName,
					m.Margin.ToString(),
					m.Mode,
					realLiqStr,
					m.AccountRuinCount.ToString(),
					balMinStr,
					balDeathStr,
					recoveredStr,
					recovDaysCalStr,
					recovSignalsStr,
					timeBelowStr,
					reqGainStr
				};

				var color = ChooseRowColorBySurvival (m);
				t.AddColoredRow (color, line);
				}

			t.WriteToConsole ();
			}

		/// <summary>
		/// Таблица средних доходностей по горизонту (день/неделя/месяц/год).
		/// Всё считается на wealth-кривой (equity + withdrawn) по календарному времени.
		/// Формат совпадает с PolicySlComparisonPrinter.PrintReturnHorizonTable.
		/// </summary>
		private static void PrintReturnHorizonTable (
			string title,
			IReadOnlyList<PolicySlMetrics.PolicyRowMetrics> rows )
			{
			ConsoleStyler.WriteHeader (title);

			var t = new TextTable ();
			t.AddHeader (
				"Policy",
				"Margin",
				"Mode",
				"Horizon days",
				"Avg/day %",
				"Avg/week %",
				"Avg/month %",
				"Avg/year %"
			);

			foreach (var m in rows)
				{
				string horizonStr = m.HorizonDays > 0.0
					? $"{m.HorizonDays:0.0}"
					: "n/a";

				var line = new[]
				{
					m.PolicyName,
					m.Margin.ToString(),
					m.Mode,
					horizonStr,
					$"{m.AvgDailyPct:0.00}%",
					$"{m.AvgWeeklyPct:0.00}%",
					$"{m.AvgMonthlyPct:0.00}%",
					$"{m.AvgYearlyPct:0.00}%"
				};

				var color = ChooseRowColorBySurvival (m);
				t.AddColoredRow (color, line);
				}

			t.WriteToConsole ();
			}

		/// <summary>
		/// Логика раскраски строки:
		/// красный — если политика «умерла» по балансу / ликвидации,
		/// зелёный — если выжила.
		/// Та же, что в PolicySlComparisonPrinter.
		/// </summary>
		private static ConsoleColor ChooseRowColorBySurvival ( PolicySlMetrics.PolicyRowMetrics m )
			{
			double wealthNow = m.StartCapital + m.TotalUsd;

			bool accountDead =
				m.AccountRuinCount > 0 ||
				m.BalMinFrac <= PolicySlMetrics.BalanceDeathThresholdFrac ||
				
					m.StartCapital > 0.0 &&
					m.OnExchUsd <= 1e-6 &&
					wealthNow <= m.StartCapital * 1.001
				;

			return accountDead
				? ConsoleStyler.BadColor
				: ConsoleStyler.GoodColor;
			}
		}
	}
