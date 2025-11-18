using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.Indicators;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Delayed.Builders;
using SolSignalModel1D_Backtest.Core.ML.Delayed.Trainers;
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
using DelayedIntradayResult = SolSignalModel1D_Backtest.Core.Trading.Evaluator.DelayedIntradayResult;

namespace SolSignalModel1D_Backtest
	{
	internal class Program
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		/// <summary>
		/// Результат построения дневных строк:
		/// все строки (6h-окна) и подмножество утренних (NY morning).
		/// </summary>
		private sealed class DailyRowsBundle
			{
			public List<DataRow> AllRows { get; init; } = new List<DataRow> ();
			public List<DataRow> Mornings { get; init; } = new List<DataRow> ();
			}

		public static async Task Main ( string[] args )
			{
			Console.WriteLine ($"[paths] CandlesDir    = {PathConfig.CandlesDir}");
			Console.WriteLine ($"[paths] IndicatorsDir = {PathConfig.IndicatorsDir}");

			// --- обновляем свечи ---
			using var http = new HttpClient ();

			var solUpdater = new CandleDailyUpdater (
				http,
				"SOLUSDT",
				PathConfig.CandlesDir,
				catchupDays: 3
			);

			var btcUpdater = new CandleDailyUpdater (
				http,
				"BTCUSDT",
				PathConfig.CandlesDir,
				catchupDays: 3
			);

			var paxgUpdater = new CandleDailyUpdater (
				http,
				"PAXGUSDT",
				PathConfig.CandlesDir,
				catchupDays: 3
			);

			Console.WriteLine ("[update] Updating SOL/BTC/PAXG candles...");
			await solUpdater.UpdateAllAsync ();
			await btcUpdater.UpdateAllAsync ();
			await paxgUpdater.UpdateAllAsync ();
			Console.WriteLine ("[update] Candle update done.");

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

			// 1h SOL — нужен для SL-фич (контекст 6h → три 2h-блока + хвост 1h)
			var solAll1h = ReadAll1h (solSym);
			Console.WriteLine ($"[1h] SOL count = {solAll1h.Count}");

			// Минутки SOL: нужны и для Path-based меток, и для Delayed A, и для SL-датасета
			var sol1m = ReadAll1m (solSym);
			Console.WriteLine ($"[1m] SOL count = {sol1m.Count}");
			if (sol1m.Count == 0)
				throw new InvalidOperationException ("[init] Нет 1m свечей SOLUSDT в cache/candles.");

			// Диапазон
			var lastUtc = solAll6h.Max (c => c.OpenTimeUtc);
			var fromUtc = lastUtc.Date.AddDays (-540);
			var toUtc = lastUtc.Date;

			// Индикаторы (обновление + проверка покрытия)
			var indicators = new IndicatorsDailyUpdater (http);
			await indicators.UpdateAllAsync (fromUtc.AddDays (-90), toUtc, IndicatorsDailyUpdater.FillMode.NeutralFill);
			indicators.EnsureCoverageOrFail (fromUtc.AddDays (-90), toUtc);

			// === ДНЕВНЫЕ СТРОКИ ===
			var rowsBundle = await BuildDailyRowsAsync (
				indicators, fromUtc, toUtc,
				solAll6h, btcAll6h, paxgAll6h,
				sol1m
			);

			var allRows = rowsBundle.AllRows;
			var mornings = rowsBundle.Mornings;

			Console.WriteLine ($"[rows] mornings (NY window) = {mornings.Count}");
			if (mornings.Count == 0)
				throw new InvalidOperationException ("[rows] После фильтров нет утренних точек.");

			// Модель (пока fallback: PredictionEngine даёт reason=fallback, а дальше — эвристика)
			var engine = CreatePredictionEngineOrFallback ();

			// PredictionRecord[] + forward (из 6h) + эвристика при fallback
			var records = await LoadPredictionRecordsAsync (mornings, solAll6h, engine);
			Console.WriteLine ($"[records] built = {records.Count}");

			// Например, хотим считать стратегию при марже 200 USDT.
			// Можно вынести в конфиг/CLI, здесь — просто константа.
			double walletBalanceUsd = 200.0;

			CurrentPredictionPrinter.Print (records, solAll6h, walletBalanceUsd);

			// === SL-модель: оффлайн-тренировка + проставление SlProb/SlHighDecision ===
			TrainAndApplySlModelOffline (
				allRows: allRows,
				records: records,
				sol1h: solAll1h,
				sol1m: sol1m,
				solAll6h: solAll6h
			);

			// Delayed A по минуткам 
			PopulateDelayedA (
				records: records,
				allRows: allRows,
				sol1h: solAll1h,
				solAll6h: solAll6h,
				sol1m: sol1m,
				dipFrac: 0.005,
				tpPct: 0.010,
				slPct: 0.010
			);

			// Политики (const 2/3/5/10/15/50 × Cross/Isolated + риск-политики)
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

		// ---------------- SL-модель: оффлайн-тренировка + применение ----------------

		/// <summary>
		/// Тренируем SL-модель на каузальном оффлайн-датасете (SlOfflineBuilder)
		/// и проставляем SlProb / SlHighDecision в PredictionRecord.
		/// </summary>
		private static void TrainAndApplySlModelOffline (
			List<DataRow> allRows,
			IList<PredictionRecord> records,
			IReadOnlyList<Candle1h> sol1h,
			IReadOnlyList<Candle1m> sol1m,
			IReadOnlyList<Candle6h> solAll6h )
			{
			if (allRows == null || allRows.Count == 0)
				{
				Console.WriteLine ("[sl-offline] no rows, skip SL-model.");
				return;
				}

			var sol6hDict = solAll6h.ToDictionary (c => c.OpenTimeUtc, c => c);
			var sol1hOrNull = sol1h != null && sol1h.Count > 0 ? sol1h : null;

			// Строим SL-датасет: для каждого утреннего дня — гипотетический long/short, кто был первым: SL или TP
			var slSamples = SlOfflineBuilder.Build (
				rows: allRows,
				sol1h: sol1hOrNull,
				sol1m: sol1m,
				sol6hDict: sol6hDict
			);

			Console.WriteLine ($"[sl-offline] built samples = {slSamples.Count}");
			if (slSamples.Count < 20)
				{
				Console.WriteLine ("[sl-offline] too few samples, skip SL-model.");
				return;
				}

			// Оффлайн-тренировка (без онлайн-доучивания)
			var trainer = new SlFirstTrainer ();
			var asOf = allRows.Max (r => r.Date);
			var slModel = trainer.Train (slSamples, asOf);
			var slEngine = trainer.CreateEngine (slModel);

			// Порог "HIGH" риска (positive класс = SL-first)
			const float SlRiskThreshold = 0.55f;

			// Быстрая мапа DataRow по дате
			var rowByDate = allRows.ToDictionary (r => r.Date, r => r);

			int scored = 0;

			foreach (var rec in records)
				{
				if (!rowByDate.TryGetValue (rec.DateUtc, out var row))
					continue;

				// Направление по дневной модели
				bool goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
				bool goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);
				if (!goLong && !goShort)
					continue;

				bool strong = rec.PredLabel == 2 || rec.PredLabel == 0;
				double dayMinMove = rec.MinMove > 0 ? rec.MinMove : 0.02;
				double entryPrice = rec.Entry;
				if (entryPrice <= 0) continue;

				// Фичи для SL-модели (6h-контекст через 2h-блоки + хвост 1h)
				var slFeats = SlFeatureBuilder.Build (
					entryUtc: rec.DateUtc,
					goLong: goLong,
					strongSignal: strong,
					dayMinMove: dayMinMove,
					entryPrice: entryPrice,
					candles1h: sol1hOrNull
				);

				var slPred = slEngine.Predict (new SlHitSample
					{
					Label = false,          // в рантайме не используется
					Features = slFeats,
					EntryUtc = rec.DateUtc
					});

				double p = slPred.Probability;
				bool predHigh = slPred.PredictedLabel && p >= SlRiskThreshold;

				rec.SlProb = p;
				rec.SlHighDecision = predHigh;
				scored++;
				}

			Console.WriteLine ($"[sl-runtime] scored days = {scored}/{records.Count}");
			}

		// ---------------- helpers ----------------

		private static List<RollingLoop.PolicySpec> BuildPolicies ()
			{
			var list = new List<RollingLoop.PolicySpec> ();

			void AddConst ( double lev )
				{
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

		private static List<Candle1h> ReadAll1h ( string symbol )
			{
			var path = CandlePaths.File (symbol, "1h");
			if (!File.Exists (path)) return new List<Candle1h> ();
			var store = new CandleNdjsonStore (path);
			var lines = store.ReadRange (DateTime.MinValue, DateTime.MaxValue);
			return lines.Select (l => new Candle1h
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

		private static async Task<DailyRowsBundle> BuildDailyRowsAsync (
			IndicatorsDailyUpdater indicatorsUpdater,
			DateTime fromUtc, DateTime toUtc,
			List<Candle6h> solAll6h,
			List<Candle6h> btcAll6h,
			List<Candle6h> paxgAll6h,
			List<Candle1m> sol1m )
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

			var nyTz = TimeZones.NewYork;

			// Все 6h-строки (для SL-датасета и path-based labels)
			var rows = RowBuilder.BuildRowsDaily (
				solWinTrain: solWinTrain,
				btcWinTrain: btcWinTrain,
				paxgWinTrain: paxgWinTrain,
				solAll6h: solAll6h,
				solAll1m: sol1m,
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

			var lastSolTime = solWinTrain.Max (c => c.OpenTimeUtc);
			Console.WriteLine ($"[rows] last SOL 6h = {lastSolTime:O}");

			return await Task.FromResult (new DailyRowsBundle
				{
				AllRows = rows,
				Mornings = mornings
				});
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
			// Prepare sorted 6h list for forward range calculations
			var sorted6h = solAll6h is List<Candle6h> list6h ? list6h : solAll6h.ToList ();
			int usedHeuristic = 0;
			var list = new List<PredictionRecord> (mornings.Count);
			var nyTz = TimeZones.NewYork;;

			foreach (var r in mornings)
				{
				var pr = engine.Predict (r);

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

				// Вычисляем показатели по forward-окну (до базового выхода t_exit)
				DateTime entryUtc = r.Date;
				DateTime exitUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz);

				int entryIdx = sorted6h.FindIndex (c => c.OpenTimeUtc == entryUtc);
				if (entryIdx < 0)
					throw new InvalidOperationException ($"[forward] entry candle {entryUtc:O} not found in 6h series");

				int exitIdx = -1;
				for (int i = entryIdx; i < sorted6h.Count; i++)
					{
					var start = sorted6h[i].OpenTimeUtc;
					DateTime end = (i + 1 < sorted6h.Count)
						? sorted6h[i + 1].OpenTimeUtc
						: start.AddHours (6);
					if (exitUtc >= start && exitUtc <= end)
						{
						exitIdx = i;
						break;
						}
					}
				if (exitIdx < 0)
					{
					Console.WriteLine ($"[forward] no 6h candle covering baseline exit {exitUtc:O} (entry {entryUtc:O})");
					throw new InvalidOperationException ($"[forward] no 6h candle covering baseline exit {exitUtc:O}");
					}
				if (exitIdx <= entryIdx)
					{
					throw new InvalidOperationException ($"[forward] exitIdx {exitIdx} <= entryIdx {entryIdx}");
					}

				double entryPrice = sorted6h[entryIdx].Close;
				double maxHigh = double.MinValue;
				double minLow = double.MaxValue;
				for (int j = entryIdx + 1; j <= exitIdx; j++)
					{
					var c = sorted6h[j];
					if (c.High > maxHigh) maxHigh = c.High;
					if (c.Low < minLow) minLow = c.Low;
					}
				if (maxHigh == double.MinValue || minLow == double.MaxValue)
					{
					throw new InvalidOperationException ($"[forward] no candles between entry {entryUtc:O} and exit {exitUtc:O}");
					}
				double fwdClose = sorted6h[exitIdx].Close;

				list.Add (new PredictionRecord
					{
					DateUtc = r.Date,
					TrueLabel = r.Label,
					PredLabel = cls,

					PredMicroUp = microUp,
					PredMicroDown = microDn,
					FactMicroUp = r.FactMicroUp,
					FactMicroDown = r.FactMicroDown,

					Entry = entryPrice,
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

		private static (int Class, bool MicroUp, bool MicroDown, string Reason) HeuristicPredict ( DataRow r )
			{
			double up = 0, dn = 0;

			if (r.SolEma50vs200 > 0.005) up += 1.2;
			if (r.SolEma50vs200 < -0.005) dn += 1.2;
			if (r.BtcEma50vs200 > 0.0) up += 0.6;
			if (r.BtcEma50vs200 < 0.0) dn += 0.6;

			if (r.SolRet3 > 0) up += 0.7; else if (r.SolRet3 < 0) dn += 0.7;
			if (r.SolRet1 > 0) up += 0.4; else if (r.SolRet1 < 0) dn += 0.4;

			if (r.SolRsiCentered > +4) up += 0.7;
			if (r.SolRsiCentered < -4) dn += 0.7;

			if (r.BtcRet30 > 0) up += 0.3; else if (r.BtcRet30 < 0) dn += 0.3;
			if (r.DxyChg30 > 0.01) dn += 0.2;
			if (r.GoldChg30 > 0.01) dn += 0.1;

			double gap = Math.Abs (up - dn);
			bool move = (up >= 1.8 || dn >= 1.8) && gap >= 0.6;

			if (move)
				{
				return (up >= dn ? 2 : 0, false, false, $"move:{(up >= dn ? "up" : "down")}, u={up:0.00}, d={dn:0.00}");
				}
			else
				{
				bool microUp = up > dn + 0.3;
				bool microDn = dn > up + 0.3;

				if (!microUp && !microDn)
					{
					if (r.RsiSlope3 > +8) microUp = true;
					else if (r.RsiSlope3 < -8) microDn = true;
					}

				return (1, microUp, microDn, $"flat: u={up:0.00} d={dn:0.00} rsiSlope={r.RsiSlope3:0.0}");
				}
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
			List<DataRow> allRows,
			IReadOnlyList<Candle1h> sol1h,
			IReadOnlyList<Candle6h> solAll6h,
			IReadOnlyList<Candle1m> sol1m,
			double dipFrac = 0.005,   // 0.5% откат для входа
			double tpPct = 0.010,     // базовый 1.0% TP
			double slPct = 0.010 )    // базовый 1.0% SL
			{
			if (records == null || records.Count == 0)
				return;

			if (sol1m == null || sol1m.Count == 0)
				throw new InvalidOperationException ("[PopulateDelayedA] Отсутствуют 1m свечи SOLUSDT для моделирования отложенных входов.");

			if (allRows == null || allRows.Count == 0)
				{
				Console.WriteLine ("[PopulateDelayedA] нет allRows — пропускаем модель A.");
				return;
				}

			if (sol1h == null || sol1h.Count == 0)
				{
				Console.WriteLine ("[PopulateDelayedA] нет sol1h — пропускаем модель A.");
				return;
				}

			if (solAll6h == null || solAll6h.Count == 0)
				{
				Console.WriteLine ("[PopulateDelayedA] нет solAll6h — пропускаем модель A.");
				return;
				}

			// Словарь 6h для оффлайн-датасета модели A
			var sol6hDict = solAll6h.ToDictionary (c => c.OpenTimeUtc, c => c);

			// *** Оффлайн-датасет для модели A (deep pullback) ***
			List<PullbackContinuationSample> pullbackSamples = PullbackContinuationOfflineBuilder.Build (
				rows: allRows,
				sol1h: sol1h,
				sol6hDict: sol6hDict
			);

			Console.WriteLine ($"[PopulateDelayedA] built pullbackSamples = {pullbackSamples.Count}");

			if (pullbackSamples.Count == 0)
				{
				Console.WriteLine ("[PopulateDelayedA] Нет выборки для обучения модели A – пропускаем.");
				return;
				}

			// Обучаем модель A на всей выборке (каузально: asOf = последняя дата + 1 день)
			var pullbackTrainer = new PullbackContinuationTrainer ();
			DateTime asOfDate = allRows.Max (r => r.Date).AddDays (1);
			var pullbackModel = pullbackTrainer.Train (pullbackSamples, asOfDate);
			var pullbackEngine = pullbackTrainer.CreateEngine (pullbackModel);

			// *** Итерируем по каждому дню и решаем, использовать ли отложенный вход A. ***
			foreach (var rec in records)
				{
				// Направление дневной сделки
				bool wantLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
				bool wantShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);

				if (!wantLong && !wantShort)
					{
					rec.DelayedEntryUsed = false;
					continue;
					}

				// 1. Гейт по SL-модели: используем A только если SL-модель считает день рискованным
				if (!rec.SlHighDecision)
					{
					rec.DelayedEntryUsed = false;
					continue;
					}

				// dayStart = NY-утро (через PredictionRecord.DateUtc)
				DateTime dayStart = rec.DateUtc;

				// t_exit (baseline) = следующее рабочее NY-утро 08:00 (минус 2 минуты) в UTC.
				// Всё, что связано с delayed A, живёт в окне [dayStart; dayEnd).
				DateTime dayEnd = Windowing.ComputeBaselineExitUtc (dayStart);

				bool strongSignal = (rec.PredLabel == 2 || rec.PredLabel == 0);
				double dayMinMove = rec.MinMove > 0 ? rec.MinMove : 0.02;

				// 1h внутри baseline-окна — для фич модели A
				var dayHours = sol1h
					.Where (h => h.OpenTimeUtc >= dayStart && h.OpenTimeUtc < dayEnd)
					.OrderBy (h => h.OpenTimeUtc)
					.ToList ();

				if (dayHours.Count == 0)
					{
					rec.DelayedEntryUsed = false;
					rec.DelayedWhyNot = "no 1h candles";
					continue;
					}

				// Фичи модели A: смотрим на тот же baseline-интервал, что и реальные таргеты/деньги
				var features = TargetLevelFeatureBuilder.Build (
					dayStart,        // дата/время входа (начало baseline-окна)
					wantLong,        // направление
					strongSignal,    // сильный ли сигнал
					dayMinMove,      // MinMove дня
					rec.Entry,       // дневная цена входа
					dayHours         // 1h-свечи baseline-интервала
				);

				var pullbackSample = new PullbackContinuationSample
					{
					Features = features,
					Label = false,      // в рантайме не используется
					EntryUtc = dayStart
					};

				var predA = pullbackEngine.Predict (pullbackSample);

				// 2. Гейт по модели A: она должна сказать "да, откат имеет смысл" с достаточной уверенностью
				if (!predA.PredictedLabel || predA.Probability < 0.70f)
					{
					rec.DelayedEntryUsed = false;
					continue;
					}

				// 3. Если дошли сюда — A сказала "да", SL сказал "рискованно" — пробуем отложенный вход.
				rec.DelayedSource = "A";
				rec.DelayedEntryAsked = true;
				rec.DelayedEntryUsed = true;

				// Минутки внутри baseline-окна
				var dayMinutes = sol1m
					.Where (m => m.OpenTimeUtc >= dayStart && m.OpenTimeUtc < dayEnd)
					.OrderBy (m => m.OpenTimeUtc)
					.ToList ();

				if (dayMinutes.Count == 0)
					{
					rec.DelayedEntryUsed = false;
					rec.DelayedWhyNot = "no 1m candles";
					continue;
					}

				// Цена триггера (глубина отката = dipFrac от цены входа)
				double triggerPrice = wantLong
					? rec.Entry * (1.0 - dipFrac)
					: rec.Entry * (1.0 + dipFrac);

				// Максимальная задержка — maxDelayHours от dayStart (обычно 4 часа)
				DateTime maxDelayTime = dayStart.AddHours (4);
				Candle1m? fillBar = null;

				foreach (var m in dayMinutes)
					{
					if (m.OpenTimeUtc > maxDelayTime)
						break;

					if (wantLong && m.Low <= triggerPrice)
						{
						fillBar = m;
						break;
						}

					if (wantShort && m.High >= triggerPrice)
						{
						fillBar = m;
						break;
						}
					}

				if (fillBar == null)
					{
					// цена отката не достигнута — вход не исполнился
					rec.DelayedEntryExecuted = false;
					rec.DelayedWhyNot = "no trigger";
					continue;
					}

				// Фиксируем факт исполнения
				rec.DelayedEntryExecuted = true;
				rec.DelayedEntryExecutedAtUtc = fillBar.OpenTimeUtc;
				rec.DelayedEntryPrice = triggerPrice;

				// Привязка TP/SL к MinMove
				double effectiveTpPct = tpPct;
				double effectiveSlPct = slPct;

				if (rec.MinMove > 0.0)
					{
					double linkedTp = rec.MinMove * 1.2; // коэффициент 1.2 от MinMove для TP
					if (linkedTp > effectiveTpPct)
						effectiveTpPct = linkedTp;
					}

				rec.DelayedIntradayTpPct = effectiveTpPct;
				rec.DelayedIntradaySlPct = effectiveSlPct;

				// Уровни TP/SL от цены фактического исполнения
				double tpLevel = wantLong
					? rec.DelayedEntryPrice * (1.0 + effectiveTpPct)
					: rec.DelayedEntryPrice * (1.0 - effectiveTpPct);

				double slLevel = wantLong
					? rec.DelayedEntryPrice * (1.0 - effectiveSlPct)
					: rec.DelayedEntryPrice * (1.0 + effectiveSlPct);

				// Определяем, что сработало первым, в окне [ExecutedAt; dayEnd)
				rec.DelayedIntradayResult = (int) DelayedIntradayResult.None;

				foreach (var m in dayMinutes)
					{
					if (m.OpenTimeUtc < rec.DelayedEntryExecutedAtUtc)
						continue;

					bool hitTp = wantLong ? (m.High >= tpLevel) : (m.Low <= tpLevel);
					bool hitSl = wantLong ? (m.Low <= slLevel) : (m.High >= slLevel);

					if (hitTp && hitSl)
						{
						rec.DelayedIntradayResult = (int) DelayedIntradayResult.Ambiguous;
						break;
						}

					if (hitTp)
						{
						rec.DelayedIntradayResult = (int) DelayedIntradayResult.TpFirst;
						break;
						}

					if (hitSl)
						{
						rec.DelayedIntradayResult = (int) DelayedIntradayResult.SlFirst;
						break;
						}
					}
				}
			}
		}
	}
