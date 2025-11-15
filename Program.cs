using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.Indicators;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest
	{
	internal class Program
		{
		public static async Task Main ( string[] args )
			{
			Console.WriteLine ($"[paths] CandlesDir    = {PathConfig.CandlesDir}");
			Console.WriteLine ($"[paths] IndicatorsDir = {PathConfig.IndicatorsDir}");

			// Символы без слэшей, чтобы совпадали с именами файлов в cache/candles/
			var solSym = "SOLUSDT";
			var btcSym = "BTCUSDT";
			var paxgSym = "PAXGUSDT";

			// Обеспечиваем наличие 6h (ресэмплинг из 1h/1m при надобности)
			CandleResampler.Ensure6hAvailable (solSym);
			CandleResampler.Ensure6hAvailable (btcSym);
			CandleResampler.Ensure6hAvailable (paxgSym); 

			// Читаем 6h
			var solAll6h = ReadAll6h (solSym);
			var btcAll6h = ReadAll6h (btcSym);
			var paxgAll6h = ReadAll6h (paxgSym);

			if (solAll6h.Count == 0 || btcAll6h.Count == 0 || paxgAll6h.Count == 0)
				throw new InvalidOperationException ("[init] Пустые 6h серии: SOL/BTC/PAXG. Проверь cache/candles/*.ndjson");

			Console.WriteLine ($"[6h] SOL={solAll6h.Count}, BTC={btcAll6h.Count}, PAXG={paxgAll6h.Count}");

			// Диапазон
			var lastUtc = solAll6h.Max (c => c.OpenTimeUtc);
			var fromUtc = lastUtc.Date.AddDays (-540);
			var toUtc = lastUtc.Date;

			// Индикаторы (обновление + проверка покрытия)
			using var http = new HttpClient ();
			var indicators = new IndicatorsDailyUpdater (http);
			await indicators.UpdateAllAsync (fromUtc.AddDays (-90), toUtc, IndicatorsDailyUpdater.FillMode.NeutralFill);
			indicators.EnsureCoverageOrFail (fromUtc.AddDays (-90), toUtc);

			// === ДНЕВНЫЕ СТРОКИ ===
			var mornings = await BuildDailyRowsAsync (
				indicators, fromUtc, toUtc,
				solAll6h, btcAll6h, paxgAll6h
			);

			Console.WriteLine ($"[rows] mornings (NY window) = {mornings.Count}");
			if (mornings.Count == 0)
				throw new InvalidOperationException ("[rows] После фильтров нет утренних точек.");

			// Модель (fallback)
			var engine = CreatePredictionEngineOrFallback ();

			// PredictionRecord[] + forward (из 6h), + эвристика при fallback
			var records = await LoadPredictionRecordsAsync (mornings, solAll6h, engine);
			Console.WriteLine ($"[records] built = {records.Count}");

			// Минутки SOL для PnL
			var sol1m = ReadAll1m (solSym);
			Console.WriteLine ($"[1m] SOL count = {sol1m.Count}");
			if (sol1m.Count == 0)
				throw new InvalidOperationException ("[init] Нет 1m свечей SOLUSDT в cache/candles.");

			PopulateDelayedA (records, sol1m, dipFrac: 0.005, tpPct: 0.010, slPct: 0.010);

			// Политики (не режу: const 2/3/5/10/15/50 × Cross/Isolated + риск-политики)
			var policies = BuildPolicies ();
			Console.WriteLine ($"[policies] total = {policies.Count}");

			// Запуск верхнеуровневого раннера (печатает confusion/micro/PNL)
			var runner = new BacktestRunner ();
			runner.Run (
				mornings: mornings,
				records: records,
				candles1m: sol1m,
				policies: policies,
				cfg: new BacktestRunner.Config { DailyStopPct = 0.05, DailyTpPct = 0.03 }
			);
			}

		// ---------------- helpers ----------------

		private static List<RollingLoop.PolicySpec> BuildPolicies ()
			{
			var list = new List<RollingLoop.PolicySpec> ();

			void AddConst ( double lev )
				{
				// Если у тебя класс называется иначе (например, ConstLeveragePolicy),
				// просто замени на него в двух местах ниже.
				var name = $"const_{lev:0.#}x";
				var policy = new LeveragePolicies.ConstPolicy (name, lev);
				list.Add (new RollingLoop.PolicySpec { Name = $"{name} Cross", Policy = policy, Margin = MarginMode.Cross });
				list.Add (new RollingLoop.PolicySpec { Name = $"{name} Isolated", Policy = policy, Margin = MarginMode.Isolated });
				}

			// фиксированные плечи
			AddConst (2.0);
			AddConst (3.0);
			AddConst (5.0);
			AddConst (10.0);
			AddConst (15.0);
			AddConst (50.0);

			// риск-осознанная
			var riskAware = new LeveragePolicies.RiskAwarePolicy ();
			list.Add (new RollingLoop.PolicySpec { Name = $"{riskAware.Name} Cross", Policy = riskAware, Margin = MarginMode.Cross });
			list.Add (new RollingLoop.PolicySpec { Name = $"{riskAware.Name} Isolated", Policy = riskAware, Margin = MarginMode.Isolated });

			// ультра-безопасная
			var ultraSafe = new LeveragePolicies.UltraSafePolicy ();
			list.Add (new RollingLoop.PolicySpec { Name = $"{ultraSafe.Name} Cross", Policy = ultraSafe, Margin = MarginMode.Cross });
			list.Add (new RollingLoop.PolicySpec { Name = $"{ultraSafe.Name} Isolated", Policy = ultraSafe, Margin = MarginMode.Isolated });

			return list;
			}

		private static List<Candle6h> ReadAll6h ( string symbol )
			{
			var path = CandlePaths.File (symbol, "6h");
			if (!File.Exists (path)) return new List<Candle6h> ();
			var store = new CandleNdjsonStore (path);
			var lines = store.ReadRange (DateTime.MinValue, DateTime.MaxValue);
			return lines.Select (l => new Candle6h
				{
				OpenTimeUtc = l.OpenTimeUtc,
				Open = l.Open,
				High = l.High,
				Low = l.Low,
				Close = l.Close
				}).OrderBy (c => c.OpenTimeUtc).ToList ();
			}

		private static List<Candle1m> ReadAll1m ( string symbol )
			{
			var path = CandlePaths.File (symbol, "1m");
			if (!File.Exists (path)) return new List<Candle1m> ();
			var store = new CandleNdjsonStore (path);
			var lines = store.ReadRange (DateTime.MinValue, DateTime.MaxValue);
			return lines.Select (l => new Candle1m
				{
				OpenTimeUtc = l.OpenTimeUtc,
				Open = l.Open,
				High = l.High,
				Low = l.Low,
				Close = l.Close
				}).OrderBy (c => c.OpenTimeUtc).ToList ();
			}

		private static async Task<List<DataRow>> BuildDailyRowsAsync (
			IndicatorsDailyUpdater indicatorsUpdater,
			DateTime fromUtc, DateTime toUtc,
			List<Candle6h> solAll6h,
			List<Candle6h> btcAll6h,
			List<Candle6h> paxgAll6h )
			{
			var histFrom = fromUtc.AddDays (-90);

			var solWinTrainRaw = solAll6h.Where (c => c.OpenTimeUtc >= histFrom && c.OpenTimeUtc <= toUtc).ToList ();
			var btcWinTrainRaw = btcAll6h.Where (c => c.OpenTimeUtc >= histFrom && c.OpenTimeUtc <= toUtc).ToList ();
			var paxgWinTrainRaw = paxgAll6h.Where (c => c.OpenTimeUtc >= histFrom && c.OpenTimeUtc <= toUtc).ToList ();

			Console.WriteLine ($"[win6h:raw] sol={solWinTrainRaw.Count}, btc={btcWinTrainRaw.Count}, paxg={paxgWinTrainRaw.Count}");

			// Тройное выравнивание: SOL ∩ BTC ∩ PAXG
			var common = solWinTrainRaw.Select (c => c.OpenTimeUtc)
				.Intersect (btcWinTrainRaw.Select (c => c.OpenTimeUtc))
				.Intersect (paxgWinTrainRaw.Select (c => c.OpenTimeUtc))
				.ToHashSet ();

			var solWinTrain = solWinTrainRaw.Where (c => common.Contains (c.OpenTimeUtc)).ToList ();
			var btcWinTrain = btcWinTrainRaw.Where (c => common.Contains (c.OpenTimeUtc)).ToList ();
			var paxgWinTrain = paxgWinTrainRaw.Where (c => common.Contains (c.OpenTimeUtc)).ToList ();

			Console.WriteLine ($"[win6h:aligned] sol={solWinTrain.Count}, btc={btcWinTrain.Count}, paxg={paxgWinTrain.Count}, common={common.Count}");

			// Индикаторы
			var fngDict = indicatorsUpdater.LoadFngDict (histFrom.Date, toUtc.Date);
			var dxyDict = indicatorsUpdater.LoadDxyDict (histFrom.Date, toUtc.Date);
			indicatorsUpdater.EnsureCoverageOrFail (histFrom.Date, toUtc.Date);

			var nyTz = GetNyTimeZone ();

			var rows = RowBuilder.BuildRowsDaily (
				solWinTrain: solWinTrain,
				btcWinTrain: btcWinTrain,
				paxgWinTrain: paxgWinTrain,
				solAll6h: solAll6h,
				fngHistory: fngDict,
				dxySeries: dxyDict,
				extraDaily: null,
				nyTz: nyTz
			);

			Console.WriteLine ($"[rows] total built = {rows.Count}");
			DumpNyHourHistogram (rows, nyTz);

			var mornings = rows
				.Where (r => r.IsMorning && r.Date >= fromUtc && r.Date < toUtc)
				.OrderBy (r => r.Date)
				.ToList ();

			Console.WriteLine ($"[rows] mornings after filter = {mornings.Count}");
			return await Task.FromResult (mornings);
			}

		private static void DumpNyHourHistogram ( List<DataRow> rows, TimeZoneInfo nyTz )
			{
			if (rows.Count == 0) return;
			var hist = new Dictionary<int, int> ();
			foreach (var r in rows)
				{
				var ny = TimeZoneInfo.ConvertTimeFromUtc (r.Date, nyTz);
				if (!hist.TryGetValue (ny.Hour, out var cnt)) cnt = 0;
				hist[ny.Hour] = cnt + 1;
				}
			Console.WriteLine ("[rows] NY hour histogram (all 6h rows, до утреннего фильтра): " +
				string.Join (", ", hist.OrderBy (kv => kv.Key).Select (kv => $"{kv.Key:D2}:{kv.Value}")));
			}

		private static async Task<List<PredictionRecord>> LoadPredictionRecordsAsync (
			IReadOnlyList<DataRow> mornings,
			IReadOnlyList<Candle6h> solAll6h,
			PredictionEngine engine )
			{
			var dict = solAll6h.ToDictionary (c => c.OpenTimeUtc, c => c);
			var list = new List<PredictionRecord> (mornings.Count);

			int usedHeuristic = 0;

			foreach (var r in mornings)
				{
				var pr = engine.Predict (r);

				// Если у нас fallback (моделей нет) — применяем эвристику,
				// чтобы не было "всегда flat" и чтобы работал PnL.
				int cls = pr.Class;
				bool microUp = pr.Micro.ConsiderUp;
				bool microDn = pr.Micro.ConsiderDown;
				string reason = pr.Reason;

				if (string.Equals (pr.Reason, "fallback", StringComparison.OrdinalIgnoreCase))
					{
					var h = HeuristicPredict (r);
					cls = h.Class;
					microUp = h.MicroUp;
					microDn = h.MicroDown;
					reason = $"heur:{h.Reason}";
					usedHeuristic++;
					}

				var (entry, maxHigh, minLow, fwdClose) = ComputeForwardFrom6h (dict, r.Date);

				list.Add (new PredictionRecord
					{
					DateUtc = r.Date,
					TrueLabel = r.Label,
					PredLabel = cls,

					PredMicroUp = microUp,
					PredMicroDown = microDn,
					FactMicroUp = r.FactMicroUp,
					FactMicroDown = r.FactMicroDown,

					Entry = entry,
					MaxHigh24 = maxHigh,
					MinLow24 = minLow,
					Close24 = fwdClose,

					RegimeDown = r.RegimeDown,
					Reason = reason,
					MinMove = r.MinMove,

					DelayedSource = string.Empty,
					DelayedEntryAsked = false,
					DelayedEntryUsed = false,
					DelayedEntryExecuted = false,
					DelayedEntryPrice = 0.0,
					DelayedIntradayResult = 0,
					DelayedIntradayTpPct = 0.0,
					DelayedIntradaySlPct = 0.0,
					TargetLevelClass = 0,
					DelayedWhyNot = null,
					DelayedEntryExecutedAtUtc = null,

					SlProb = 0.0,
					SlHighDecision = false
					});
				}

			Console.WriteLine ($"[predict] heuristic applied = {usedHeuristic}/{mornings.Count}");
			return await Task.FromResult (list);
			}

		/// <summary>
		/// Простая эвристика на фичах ряда, когда ML недоступен:
		/// - решаем move vs flat и направление,
		/// - если flat, даём micro (наклон), чтобы PnL мог торговать.
		/// </summary>
		private static (int Class, bool MicroUp, bool MicroDown, string Reason) HeuristicPredict ( DataRow r )
			{
			// Счета “за” вверх/вниз.
			double up = 0, dn = 0;

			// Тренд по EMA 50/200 (SOL и BTC)
			if (r.SolEma50vs200 > 0.005) up += 1.2;
			if (r.SolEma50vs200 < -0.005) dn += 1.2;
			if (r.BtcEma50vs200 > 0.0) up += 0.6;
			if (r.BtcEma50vs200 < 0.0) dn += 0.6;

			// Короткие ретёрны
			if (r.SolRet3 > 0) up += 0.7; else if (r.SolRet3 < 0) dn += 0.7;
			if (r.SolRet1 > 0) up += 0.4; else if (r.SolRet1 < 0) dn += 0.4;

			// RSI (центрирован)
			if (r.SolRsiCentered > +4) up += 0.7;
			if (r.SolRsiCentered < -4) dn += 0.7;

			// Фон BTC и DXY/Gold как слабые факторы
			if (r.BtcRet30 > 0) up += 0.3; else if (r.BtcRet30 < 0) dn += 0.3;
			if (r.DxyChg30 > 0.01) dn += 0.2; // сильный доллар чаще давит
			if (r.GoldChg30 > 0.01) dn += 0.1; // “risk-off” прокси

			// Решение: движение или flat
			double gap = Math.Abs (up - dn);
			bool move = (up >= 1.8 || dn >= 1.8) && gap >= 0.6; // надо и сила, и разделимость

			if (move)
				{
				return (up >= dn ? 2 : 0, false, false, $"move:{(up >= dn ? "up" : "down")}, u={up:0.00}, d={dn:0.00}");
				}
			else
				{
				// flat, но даём наклон для торговли micro
				bool microUp = up > dn + 0.3;
				bool microDn = dn > up + 0.3;

				// если явного наклона нет — попробуем по наклону RSI
				if (!microUp && !microDn)
					{
					if (r.RsiSlope3 > +8) microUp = true;
					else if (r.RsiSlope3 < -8) microDn = true;
					}

				return (1, microUp, microDn, $"flat: u={up:0.00} d={dn:0.00} rsiSlope={r.RsiSlope3:0.0}");
				}
			}

		/// <summary>Берём 4 следующих 6h свечи [t, t+24h)</summary>
		private static (double entry, double maxHigh, double minLow, double fwdClose)
			ComputeForwardFrom6h ( Dictionary<DateTime, Candle6h> sol6h, DateTime t )
			{
			if (!sol6h.TryGetValue (t, out var c0))
				throw new InvalidOperationException ($"[forward] нет 6h свечи @ {t:o}");

			var t1 = t.AddHours (6);
			var t2 = t.AddHours (12);
			var t3 = t.AddHours (18);
			var t4 = t.AddHours (24);

			if (!sol6h.TryGetValue (t1, out var c1) ||
				!sol6h.TryGetValue (t2, out var c2) ||
				!sol6h.TryGetValue (t3, out var c3) ||
				!sol6h.TryGetValue (t4, out var c4))
				throw new InvalidOperationException ($"[forward] неполные 24h окна от {t:o}");

			double entry = c0.Close;
			double maxH = new[] { c1.High, c2.High, c3.High, c4.High }.Max ();
			double minL = new[] { c1.Low, c2.Low, c3.Low, c4.Low }.Min ();
			double fwdClose = c4.Close;
			return (entry, maxH, minL, fwdClose);
			}

		private static TimeZoneInfo GetNyTimeZone ()
			{
			try { return TimeZoneInfo.FindSystemTimeZoneById ("America/New_York"); }
			catch { return TimeZoneInfo.FindSystemTimeZoneById ("Eastern Standard Time"); }
			}

		private static PredictionEngine CreatePredictionEngineOrFallback ()
			{
			var bundle = new ModelBundle
				{
				MlCtx = null,
				MoveModel = null,
				DirModelNormal = null,
				DirModelDown = null,
				MicroFlatModel = null
				};
			return new PredictionEngine (bundle);
			}

		private static void PopulateDelayedA (
		   IList<PredictionRecord> records,
		   IReadOnlyList<Candle1m> sol1m,
		   double dipFrac = 0.005,   // 0.5% откат для входа
		   double tpPct = 0.010,   // 1.0% TP
		   double slPct = 0.010 )   // 1.0% SL
			{
			if (records == null || records.Count == 0) return;
			var m1 = sol1m.OrderBy (m => m.OpenTimeUtc).ToList ();

			foreach (var r in records)
				{
				r.DelayedSource = "A";
				r.DelayedEntryAsked = true;
				r.DelayedEntryUsed = true;

				bool wantLong = r.PredLabel == 2 || (r.PredLabel == 1 && r.PredMicroUp);
				bool wantShort = r.PredLabel == 0 || (r.PredLabel == 1 && r.PredMicroDown);
				if (!wantLong && !wantShort) { r.DelayedEntryUsed = false; continue; }

				DateTime from = r.DateUtc;
				DateTime to = r.DateUtc.AddHours (24);
				var dayMins = m1.Where (m => m.OpenTimeUtc >= from && m.OpenTimeUtc < to).ToList ();
				if (dayMins.Count == 0) { r.DelayedEntryUsed = false; continue; }

				double trigger = wantLong ? r.Entry * (1.0 - dipFrac) : r.Entry * (1.0 + dipFrac);
				Candle1m? trig = null;

				foreach (var m in dayMins)
					{
					if (wantLong && m.Low <= trigger) { trig = m; break; }
					if (wantShort && m.High >= trigger) { trig = m; break; }
					}

				if (trig == null)
					{
					r.DelayedEntryExecuted = false;
					r.DelayedWhyNot = "no trigger";
					continue;
					}

				r.DelayedEntryExecuted = true;
				r.DelayedEntryExecutedAtUtc = trig.OpenTimeUtc;
				r.DelayedEntryPrice = wantLong ? trigger : trigger;
				r.DelayedIntradayTpPct = tpPct;
				r.DelayedIntradaySlPct = slPct;

				double tp = wantLong ? r.DelayedEntryPrice * (1.0 + tpPct)
									 : r.DelayedEntryPrice * (1.0 - tpPct);
				double sl = wantLong ? r.DelayedEntryPrice * (1.0 - slPct)
									 : r.DelayedEntryPrice * (1.0 + slPct);

				int res = 0; // 0 => ни TP, ни SL: закроем по close дня
				foreach (var m in dayMins.Where (x => x.OpenTimeUtc >= trig.OpenTimeUtc))
					{
					bool hitTp = wantLong ? m.High >= tp : m.Low <= tp;
					bool hitSl = wantLong ? m.Low <= sl : m.High >= sl;

					if (hitTp && hitSl)
						{
						// считаем TP приоритетным, если оба могли случиться в одной минуте
						res = (int) DelayedIntradayResult.TpFirst;
						break;
						}
					if (hitTp) { res = (int) DelayedIntradayResult.TpFirst; break; }
					if (hitSl) { res = (int) DelayedIntradayResult.SlFirst; break; }
					}
				r.DelayedIntradayResult = res;
				}
			}
			}
	}
