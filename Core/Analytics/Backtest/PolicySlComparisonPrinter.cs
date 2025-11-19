using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Backtest;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest
	{
	/// <summary>
	/// Сравнение политик WITH SL vs WITHOUT SL в трёх таблицах:
	/// 1) PnL-таблица (Total %, Total $, Withdrawn, OnExch и т.д.).
	/// 2) Risk-таблица (ликвидации, BalMin, время ниже порога, восстановление из MaxDD).
	/// 3) Доходности по горизонту (день/неделя/месяц/год) на основе wealth-кривой.
	/// Все расчёты вынесены в PolicySlMetrics; здесь только сборка и печать.
	/// </summary>
	public static class PolicySlComparisonPrinter
		{
		public static void Print (
			IReadOnlyList<BacktestPolicyResult> withSl,
			IReadOnlyList<BacktestPolicyResult> noSl )
			{
			if (withSl == null || withSl.Count == 0) return;

			// 1) Собираем метрики для WITH SL и соответствующих NO SL.
			var rows = new List<PolicySlMetrics.PolicyRowMetrics> ();

			foreach (var w in withSl
				.OrderBy (x => x.PolicyName)
				.ThenBy (x => x.Margin.ToString ()))
				{
				// WITH SL
				rows.Add (PolicySlMetrics.BuildMetrics (w, "with SL"));

				// Пара "без SL", если есть.
				var n = noSl?
					.FirstOrDefault (x => x.PolicyName == w.PolicyName && x.Margin.Equals (w.Margin));

				if (n != null)
					rows.Add (PolicySlMetrics.BuildMetrics (n, "no SL"));
				}

			if (rows.Count == 0) return;

			// 2) Таблица PnL
			PrintPnlTable (rows);

			// 3) Таблица риска / ликвидаций
			PrintRiskTable (rows);

			// 4) Таблица средних доходностей по горизонту
			PrintReturnHorizonTable (rows);
			}

		/// <summary>
		/// Основная PnL-таблица:
		/// Policy / Margin / Mode / Trades / Total% / Total$ / MaxDD% / Withdrawn / OnExch$ /
		/// Long/Short n/$ и средние проценты.
		/// </summary>
		private static void PrintPnlTable ( IReadOnlyList<PolicySlMetrics.PolicyRowMetrics> rows )
			{
			ConsoleStyler.WriteHeader ("=== Policies: WITH SL vs WITHOUT SL (PnL) ===");

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
		/// Для ликвидаций выводится:
		/// - RealLiq # — количество сделок с флагом IsRealLiquidation (для isolated; для cross печатается «—»);
		/// - AccRuin   — факт хотя бы одного «руин-события» на уровне политики.
		/// </summary>
		private static void PrintRiskTable ( IReadOnlyList<PolicySlMetrics.PolicyRowMetrics> rows )
			{
			ConsoleStyler.WriteHeader ("=== Policies: Risk / Drawdown metrics ===");

			var t = new TextTable ();
			t.AddHeader (
				"Policy",
				"Margin",
				"Mode",
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
						: (double.IsPositiveInfinity (m.ReqGainPct) ? "INF" : "0.0%");

				// Реальные ликвидации выводим только для isolated.
				// Для cross — просто тире, т.к. там 1 ликвидация = фактически смерть.
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
		/// </summary>
		private static void PrintReturnHorizonTable ( IReadOnlyList<PolicySlMetrics.PolicyRowMetrics> rows )
			{
			ConsoleStyler.WriteHeader ("=== Policies: Avg returns by horizon (calendar) ===");

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
		/// Условие смерти:
		/// - была account liquidation (AccRuin > 0), ИЛИ
		/// - баланс падал ниже 35% от старта (BalMinFrac &lt;= threshold), ИЛИ
		/// - к концу OnExch ≈ 0 и суммарное состояние wealthNow не выше старта.
		/// </summary>
		private static ConsoleColor ChooseRowColorBySurvival ( PolicySlMetrics.PolicyRowMetrics m )
			{
			double wealthNow = m.StartCapital + m.TotalUsd;

			bool accountDead =
				m.AccountRuinCount > 0 ||
				m.BalMinFrac <= PolicySlMetrics.BalanceDeathThresholdFrac ||
				(
					m.StartCapital > 0.0 &&
					m.OnExchUsd <= 1e-6 &&
					wealthNow <= m.StartCapital * 1.001
				);

			return accountDead
				? ConsoleStyler.BadColor
				: ConsoleStyler.GoodColor;
			}
		}
	}
