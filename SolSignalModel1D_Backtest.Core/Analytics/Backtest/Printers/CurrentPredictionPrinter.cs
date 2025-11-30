using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Печатает «текущий прогноз» последнего доступного утра (NY 08:00)
	/// и под него — два варианта торговой политики:
	///   1) RiskAwarePolicy (динамическое плечо).
	///   2) ConstPolicy с фиксированным плечом 3x.
	/// 
	/// Для каждой политики выводятся две ветки:
	///   - BASE direction: торгуем по направлению дневной модели, но
	///     пропускаем день, если SL-модель пометила его как рискованный.
	///   - ANTI-D overlay: рассматриваем только рискованные дни
	///     (если день не рискованный — overlay не используется).
	/// Направление ни в одном режиме не переворачивается.
	/// </summary>
	public static class CurrentPredictionPrinter
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		/// <summary>
		/// Печать текущего прогноза и стратегий.
		/// walletBalanceUsd — объём маржи/депозита, на который считаться размер позиции.
		/// </summary>
		public static void Print (
			IReadOnlyList<PredictionRecord> records,
			IReadOnlyList<Candle6h> solAll6h,
			double walletBalanceUsd = 200.0 )
			{
			if (records == null || records.Count == 0)
				{
				Console.WriteLine ("[current] нет PredictionRecord — нечего выводить.");
				return;
				}
			if (solAll6h == null || solAll6h.Count == 0)
				{
				Console.WriteLine ("[current] нет 6h свечей — невозможно показать forward.");
				return;
				}

			// Берём последний доступный прогноз по дате
			var last = records.OrderBy (r => r.DateUtc).Last ();

			var tbTz = TimeZoneInfo.FindSystemTimeZoneById ("Asia/Tbilisi");
			DateTime nyTime = TimeZoneInfo.ConvertTimeFromUtc (last.DateUtc, NyTz);
			DateTime tbNow = TimeZoneInfo.ConvertTimeFromUtc (DateTime.UtcNow, tbTz);

			ConsoleStyler.WriteHeader ("=== ТЕКУЩИЙ ПРОГНОЗ ===");
			Console.WriteLine ($"Дата прогноза (NY): {nyTime:yyyy-MM-dd HH:mm}");
			Console.WriteLine ($"Текущее время (Tbilisi): {tbNow:yyyy-MM-dd HH:mm}");
			Console.WriteLine ($"Predicted class: {FormatLabel (last)}");
			Console.WriteLine ($"Micro: {FormatMicro (last)}");
			Console.WriteLine ($"Regime: {(last.RegimeDown ? "DOWN" : "NORMAL")}");
			Console.WriteLine ($"SL-prob: {last.SlProb:0.00} → SlHighDecision={last.SlHighDecision}");
			Console.WriteLine ($"Entry: {last.Entry:0.0000} USDT");
			Console.WriteLine ();

			// Forward по базовому горизонту — для контекста
			if (TryComputeForwardBaseline (solAll6h, last.DateUtc, NyTz, out var fwd))
				{
				Console.WriteLine ("=== Forward (baseline NY→следующее NY-утро) ===");
				Console.WriteLine ($"MaxHigh: {fwd.maxHigh:0.0000} USDT");
				Console.WriteLine ($"MinLow:  {fwd.minLow:0.0000} USDT");
				Console.WriteLine ($"Close:   {fwd.closeExit:0.0000} USDT");
				Console.WriteLine ();
				}
			else
				{
				Console.WriteLine ("=== Forward ===");
				Console.WriteLine ("Недостаточно 6h свечей для baseline-окна (проверь последние дни).");
				Console.WriteLine ();
				}

			// === Политика 1: RiskAwarePolicy (динамическое плечо) ===
			var riskPolicy = new LeveragePolicies.RiskAwarePolicy ();
			PrintPolicyPair (last, riskPolicy, walletBalanceUsd);

			// === Политика 2: ConstPolicy 3x (фиксированное плечо) ===
			var const3xPolicy = new LeveragePolicies.ConstPolicy ("const_3x", 3.0);
			PrintPolicyPair (last, const3xPolicy, walletBalanceUsd);
			}

		/// <summary>
		/// Печатает две ветки для одной политики:
		///   - BASE direction: использует только «нерискованные» дни (по SL).
		///   - ANTI-D overlay: использует только «рискованные» дни (по SL).
		/// Направление всегда остаётся тем, что дала дневная модель.
		/// </summary>
		private static void PrintPolicyPair (
			PredictionRecord rec,
			ILeveragePolicy policy,
			double walletBalanceUsd )
			{
			bool hasDir = TryGetDirection (rec, out bool goLong, out bool goShort);
			bool isRiskDay = rec.SlHighDecision;
			double lev = policy.ResolveLeverage (rec);

			// --- BASE direction ---
			ConsoleStyler.WriteHeader ($"=== Policy {policy.GetType ().Name} — BASE direction ===");

			if (!hasDir)
				{
				Console.WriteLine ("Flat: дневной сигнал не даёт направления (нет сделки).");
				Console.WriteLine ();
				}
			else if (isRiskDay)
				{
				Console.WriteLine ("День помечен как рискованный SL-моделью.");
				Console.WriteLine ("BASE-ветка стратегии этот день пропускает (сделка не открывается).");
				Console.WriteLine ();
				}
			else
				{
				// Нерискованный день — базовая ветка берёт его на себя
				PrintTradeDetails (rec, goLong, lev, walletBalanceUsd);
				}

			// --- ANTI-D overlay ---
			ConsoleStyler.WriteHeader ($"=== Policy {policy.GetType ().Name} — ANTI-D overlay ===");

			if (!hasDir)
				{
				Console.WriteLine ("Flat: нет направления — overlay тоже ничего не делает.");
				Console.WriteLine ();
				return;
				}

			if (!isRiskDay)
				{
				Console.WriteLine ("День НЕ рискованный по SL-модели.");
				Console.WriteLine ("ANTI-D overlay для такого дня не используется (нет сделки).");
				Console.WriteLine ();
				return;
				}

			// Рискованный день — overlay рассматривает его как «свой» день.
			Console.WriteLine ("День рискованный по SL-модели.");
			Console.WriteLine ("ANTI-D overlay берёт этот день под себя (направление не переворачиваем).");
			PrintTradeDetails (rec, goLong, lev, walletBalanceUsd);
			}

		/// <summary>
		/// Выясняет, есть ли направленная сделка по дневному прогнозу.
		/// LONG: class=2 или flat + microUp.
		/// SHORT: class=0 или flat + microDown.
		/// </summary>
		private static bool TryGetDirection (
			PredictionRecord rec,
			out bool goLong,
			out bool goShort )
			{
			goLong = rec.PredLabel == 2 || rec.PredLabel == 1 && rec.PredMicroUp;
			goShort = rec.PredLabel == 0 || rec.PredLabel == 1 && rec.PredMicroDown;
			return goLong || goShort;
			}

		/// <summary>
		/// Печатает конкретный торговый план:
		/// направление, плечо, SL/TP (в % и по цене), размер позиции, реальную ликвидацию.
		/// Логика SL/TP:
		///   slPct = clamp(MinMove, 1%..4%);
		///   tpPct = max(1.5 * slPct, 1.5%).
		/// Ликвидация считается как реальная (по упрощённой формуле cross-маржи),
		/// без «ужатия», которое используется только в PnL-движке.
		/// </summary>
		private static void PrintTradeDetails (
			PredictionRecord rec,
			bool goLong,
			double leverage,
			double walletBalanceUsd )
			{
			bool goShort = !goLong;
			string dir = goLong ? "LONG" : "SHORT";
			double entry = rec.Entry;

			if (entry <= 0.0)
				{
				Console.WriteLine ("Entry <= 0 — невозможно посчитать торговый план.");
				Console.WriteLine ();
				return;
				}

			double baseMinMove = rec.MinMove > 0.0 ? rec.MinMove : 0.02;
			double slPct = baseMinMove;
			if (slPct < 0.01) slPct = 0.01;
			else if (slPct > 0.04) slPct = 0.04;

			double tpPct = slPct * 1.5;
			if (tpPct < 0.015) tpPct = 0.015;

			double slPrice = goLong
				? entry * (1.0 - slPct)
				: entry * (1.0 + slPct);

			double tpPrice = goLong
				? entry * (1.0 + tpPct)
				: entry * (1.0 - tpPct);

			Console.WriteLine ($"Направление: {dir}");
			Console.WriteLine ($"Плечо (из политики): x{leverage:0.##}");
			Console.WriteLine ($"Entry: {entry:0.0000} USDT");
			Console.WriteLine ($"Stop-loss: {slPrice:0.0000} USDT ({slPct * 100.0:0.0}%)");
			Console.WriteLine ($"Take-profit: {tpPrice:0.0000} USDT ({tpPct * 100.0:0.0}%)");

			if (walletBalanceUsd <= 0.0)
				{
				Console.WriteLine ();
				Console.WriteLine ("Маржа <= 0 — размер позиции и ликвидацию считать не будем.");
				Console.WriteLine ();
				return;
				}

			double positionUsd = walletBalanceUsd * leverage;
			double positionQty = positionUsd / entry;

			Console.WriteLine ($"Размер позиции: {positionQty:0.000} SOL (~{positionUsd:0.00}$) при марже {walletBalanceUsd:0.00}$");

			// Реалистичная оценка цены реальной ликвидации для cross-маржи
			// по упрощённой модели USDT-фьючерсов без комиссий:
			//   Q = walletBalance * L / entry
			//   Equity(P) = B + (P - entry) * Q   (для LONG)
			//   MM(P)     = mmr * |Q| * P
			//   ликвидация при Equity(P) = MM(P)
			//
			// Отсюда при фиксированном maintenance margin rate (mmr ~ 0.4%):
			//   LONG:  P_liq ≈ entry * (L - 1) / (L * (1 - mmr))
			//   SHORT: P_liq ≈ entry * (1 + L) / (L * (1 + mmr))
			//
			// Здесь считаем это "реальной" биржевой ликвидацией.
			// В PnL-движке можно отдельно зажать уровень (чуть ближе),
			// но в принтере показываем именно эту точку.
			if (leverage > 1.0)
				{
				const double mmr = 0.004; // 0.4% как грубый прокси maintenance margin rate

				double liqPrice;
				if (goLong)
					{
					liqPrice = entry * (leverage - 1.0) / (leverage * (1.0 - mmr));
					}
				else
					{
					liqPrice = entry * (1.0 + leverage) / (leverage * (1.0 + mmr));
					}

				// для понимания можно вывести и расстояние до ликвидации
				double liqDistPct = goLong
					? (entry - liqPrice) / entry * 100.0
					: (liqPrice - entry) / entry * 100.0;

				Console.WriteLine ($"Реальная ликвидация (оценка): {liqPrice:0.0000} USDT (~{liqDistPct:0.0}% от entry)");
				}
			else
				{
				Console.WriteLine ("Эффективное плечо ≤ 1x — формальной маржинальной ликвидации нет (режим ближе к споту).");
				}

			Console.WriteLine ();
			}

		private static string FormatLabel ( PredictionRecord r )
			{
			return r.PredLabel switch
				{
					0 => "0 (down)",
					1 => "1 (flat)",
					2 => "2 (up)",
					_ => r.PredLabel.ToString ()
					};
			}

		private static string FormatMicro ( PredictionRecord r )
			{
			if (r.PredLabel != 1) return "не используется (не flat)";
			if (r.PredMicroUp) return "micro UP";
			if (r.PredMicroDown) return "micro DOWN";
			return "—";
			}

		/// <summary>
		/// Вычисляет forward по базовому горизонту:
		/// entryUtc → следующая рабочая NY-утренняя граница 08:00 (минус 2 минуты).
		/// Возвращает false, если свечей не хватает для покрытия exit-окна.
		/// </summary>
		private static bool TryComputeForwardBaseline (
			IReadOnlyList<Candle6h> sol6h,
			DateTime entryUtc,
			TimeZoneInfo nyTz,
			out (double maxHigh, double minLow, double closeExit) res )
			{
			res = default;
			if (sol6h == null || sol6h.Count == 0) return false;

			// Базовый момент выхода по тем же правилам, что и в RowBuilder.
			DateTime exitUtc;
			try
				{
				exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz);
				}
			catch (Exception ex)
				{
				Console.WriteLine ($"[current] baseline exit error: {ex.Message}");
				return false;
				}

			if (exitUtc <= entryUtc)
				{
				Console.WriteLine ($"[current] exitUtc {exitUtc:o} <= entryUtc {entryUtc:o}");
				return false;
				}

			var sorted = sol6h
				.OrderBy (c => c.OpenTimeUtc)
				.ToList ();

			// Индекс свечи входа
			int entryIdx = sorted.FindIndex (c => c.OpenTimeUtc == entryUtc);
			if (entryIdx < 0)
				{
				Console.WriteLine ($"[current] no 6h entry candle @ {entryUtc:o}");
				return false;
				}

			// Индекс свечи, покрывающей exitUtc
			int exitIdx = -1;
			for (int i = 0; i < sorted.Count; i++)
				{
				var start = sorted[i].OpenTimeUtc;
				DateTime end = i + 1 < sorted.Count ? sorted[i + 1].OpenTimeUtc : start.AddHours (6);
				if (exitUtc >= start && exitUtc <= end)
					{
					exitIdx = i;
					break;
					}
				}

			if (exitIdx < 0)
				{
				Console.WriteLine ($"[current] no 6h candle covering exitUtc {exitUtc:o}");
				return false;
				}
			if (exitIdx <= entryIdx)
				{
				Console.WriteLine ($"[current] exitIdx {exitIdx} <= entryIdx {entryIdx}");
				return false;
				}

			double maxH = double.MinValue;
			double minL = double.MaxValue;

			for (int i = entryIdx + 1; i <= exitIdx; i++)
				{
				var c = sorted[i];
				if (c.High > maxH) maxH = c.High;
				if (c.Low < minL) minL = c.Low;
				}

			if (maxH == double.MinValue || minL == double.MaxValue)
				{
				Console.WriteLine ($"[current] no candles between entry {entryUtc:o} and exit {exitUtc:o}");
				return false;
				}

			double closeExit = sorted[exitIdx].Close;
			res = (maxH, minL, closeExit);
			return true;
			}
		}
	}
