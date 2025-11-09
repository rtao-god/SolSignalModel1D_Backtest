using Microsoft.ML;
using SolSignalModel1D_Backtest.Core;
using SolSignalModel1D_Backtest.Core.Analytics;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.Trading;
using SolSignalModel1D_Backtest.Core.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest
	{
	internal class Program
		{
		private const int RollingTrainDays = 260;
		private const int RollingTestDays = 60;
		private const double TpPct = 0.03;

		// SL-онлайн: учим только на прошлых оффлайн-сделках
		private const int SlMinTrainSamples = 80;
		private const int SlRetrainEvery = 30;
		private const float SlFilterThreshold = 0.67f;

		public static async Task Main ( string[] args )
			{
			Console.OutputEncoding = System.Text.Encoding.UTF8;
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

			Console.WriteLine ("[init] start SOL daily backtest (with offline SL dataset, causal)");


			var http = HttpFactory.CreateDefault ("SolSignalModel1D_Backtest/1.0");

			// ===== загрузка базовых данных =====
			Console.WriteLine ("[binance] load 6h SOL...");
			var sol6h = await DataLoading.GetBinance6h (http, "SOLUSDT", 6000);

			Console.WriteLine ("[binance] load 6h BTC...");
			var btc6h = await DataLoading.GetBinance6h (http, "BTCUSDT", 6000);

			Console.WriteLine ("[binance] load 6h PAXG (opt)...");
			var paxg6h = await DataLoading.GetBinance6h (http, "PAXGUSDT", 6000, allowNull: true);

			Console.WriteLine ("[binance] load 1h SOL (for intraday TP/SL)...");
			var sol1h = await DataLoading.GetBinance1h (http, "SOLUSDT", 8000, allowNull: true);

			if (sol6h.Count == 0 || btc6h.Count == 0)
				{
				Console.WriteLine ("[fatal] no candles");
				return;
				}

			var sol6hDict = sol6h.ToDictionary (c => c.OpenTimeUtc, c => c);
			var nyTz = TimeZones.GetNewYork ();

			// окна
			var solTrainWindows = Windowing.FilterNyTrainWindows (sol6h, nyTz);
			var solMorningWindows = Windowing.FilterNyMorningOnly (sol6h, nyTz);
			var btcTrainWindows = Windowing.FilterNyTrainWindows (btc6h, nyTz);
			var paxgTrainWindows = paxg6h != null
				? Windowing.FilterNyTrainWindows (paxg6h, nyTz)
				: new List<Candle6h> ();

			Console.WriteLine ($"[windows] SOL NY-окна (train: утро+день): {solTrainWindows.Count}");
			Console.WriteLine ($"[windows] SOL NY-окна (test: только утро): {solMorningWindows.Count}");

			Console.WriteLine ("[fng] load Fear & Greed");
			var fngHistory = await DataLoading.GetFngHistory (http);

			Console.WriteLine ("[dxy] load DXY proxy");
			DateTime oldest = solTrainWindows.First ().OpenTimeUtc.Date.AddDays (-45);
			DateTime newest = solTrainWindows.Last ().OpenTimeUtc.Date;
			var dxySeries = await DataLoading.GetDxySeries (http, oldest, newest);

			var extraDaily = DataLoading.TryLoadExtraDaily ("extra.json");

			// ===== строим дневной датасет =====
			var rows = RowBuilder.BuildRowsDaily (
				solTrainWindows,
				btcTrainWindows,
				paxgTrainWindows,
				sol6h,
				fngHistory,
				dxySeries,
				extraDaily,
				nyTz
			);

			rows = rows.OrderBy (r => r.Date).ToList ();
			Console.WriteLine ($"[dataset] строк после фильтров: {rows.Count}");

			// паддинг фич до фиксированной длины
			foreach (var r in rows)
				{
				if (r.Features == null)
					{
					r.Features = new double[MlSchema.FeatureCount];
					continue;
					}

				if (r.Features.Length < MlSchema.FeatureCount)
					{
					var arr = new double[MlSchema.FeatureCount];
					Array.Copy (r.Features, arr, r.Features.Length);
					r.Features = arr;
					}
				else if (r.Features.Length > MlSchema.FeatureCount)
					{
					var arr = new double[MlSchema.FeatureCount];
					Array.Copy (r.Features, arr, MlSchema.FeatureCount);
					r.Features = arr;
					}
				}

			// ===== оффлайн SL-датасет (гипотетический лонг/шорт каждый день) =====
			var slOfflineSamples = new List<SlHitSample> ();
			if (sol1h != null && sol1h.Count > 0)
				{
				slOfflineSamples = SlOfflineBuilder.Build (rows, sol1h, sol6hDict);
				}

			var allMorning = rows.Where (r => r.IsMorning).OrderBy (r => r.Date).ToList ();
			if (allMorning.Count == 0)
				{
				Console.WriteLine ("[warn] no morning rows");
				return;
				}

			DateTime minDate = allMorning.First ().Date;
			DateTime maxDate = allMorning.Last ().Date;

			var allRecords = new List<PredictionRecord> ();

			// ===== состояние SL-модели (онлайн) =====
			var slTrainer = new SlFirstTrainer ();
			ITransformer? slModel = null;
			PredictionEngine<SlHitSample, SlHitPrediction>? slEngine = null;
			int slSamplesAtLastTrain = 0;

			// онлайн PnL после фильтра
			double filtEq = 1.0;
			double filtPeak = 1.0;
			double filtMaxDd = 0.0;
			int filtTrades = 0;
			int filtTp = 0;
			int filtSl = 0;
			int filtSkipped = 0;

			// для анализа качества SL-модели
			double slProbSum = 0.0;
			double slProbMax = 0.0;
			int slProbCount = 0;

			Console.WriteLine ();
			Console.WriteLine ("==== ROLLING ====");

			DateTime cursor = minDate.AddDays (RollingTrainDays);
			while (true)
				{
				DateTime trainStart = cursor.AddDays (-RollingTrainDays);
				DateTime trainEnd = cursor;
				DateTime testEnd = cursor.AddDays (RollingTestDays);

				var trainRows = rows
					.Where (r => r.Date >= trainStart && r.Date < trainEnd)
					.OrderBy (r => r.Date)
					.ToList ();

				var testRows = rows
					.Where (r => r.IsMorning && r.Date >= trainEnd && r.Date < testEnd)
					.OrderBy (r => r.Date)
					.ToList ();

				if (testRows.Count == 0)
					{
					cursor = cursor.AddDays (RollingTestDays);
					if (cursor >= maxDate) break;
					continue;
					}

				var testDates = new HashSet<DateTime> (testRows.Select (r => r.Date));

				// дневную модель учим как в твоём оригинале — на всём rows, но с исключёнными датами теста
				var trainer = new ModelTrainer ();
				var bundle = trainer.TrainAll (trainRows, testDates);
				var engine = new PredictionEngine (bundle);

				Console.WriteLine ();
				Console.WriteLine ($"[roll] train: {trainStart:yyyy-MM-dd} .. {trainEnd:yyyy-MM-dd} ({trainRows.Count})");
				Console.WriteLine ($"[roll] test : {trainEnd:yyyy-MM-dd} .. {testEnd:yyyy-MM-dd} ({testRows.Count})");

				int localTested = 0;
				int localBase = 0;
				int localMicro = 0;
				int localTpTrades = 0;
				int localTpOk = 0;

				foreach (var r in testRows)
					{
					// ===== подготовим SL-модель на прошлом оффлайн-наборе =====
					var pastSl = slOfflineSamples
						.Where (s => s.EntryUtc < r.Date)
						.ToList ();

					if (pastSl.Count >= SlMinTrainSamples &&
						(slModel == null || pastSl.Count - slSamplesAtLastTrain >= SlRetrainEvery))
						{
						slModel = slTrainer.Train (pastSl, r.Date);
						slEngine = slTrainer.CreateEngine (slModel);
						slSamplesAtLastTrain = pastSl.Count;
						}

					// ===== дневной прогноз =====
					var (predClass, probs, reason, microInfo) = engine.Predict (r);

					var fwdInfo = BacktestHelpers.GetForwardInfo (r.Date, sol6hDict);
					double entry = fwdInfo.entry;
					double close24 = fwdInfo.fwdClose;
					double maxHigh = fwdInfo.maxHigh;
					double minLow = fwdInfo.minLow;

					bool hasDir =
						predClass == 2 ||
						predClass == 0 ||
						(predClass == 1 && (microInfo.ConsiderUp || microInfo.ConsiderDown));

					if (hasDir) localTpTrades++;

					bool tpHit = false;
					if (hasDir)
						{
						if (predClass == 2 || (predClass == 1 && microInfo.ConsiderUp))
							{
							double tpPrice = entry * (1.0 + TpPct);
							if (maxHigh >= tpPrice) tpHit = true;
							}
						else
							{
							double tpPrice = entry * (1.0 - TpPct);
							if (minLow <= tpPrice) tpHit = true;
							}

						if (tpHit) localTpOk++;
						}

					if (predClass == r.Label) localBase++;
					if (engine.EvalMicroAware (r, predClass, microInfo)) localMicro++;

					allRecords.Add (new PredictionRecord
						{
						DateUtc = r.Date,
						TrueLabel = r.Label,
						PredLabel = predClass,
						PredMicroUp = microInfo.ConsiderUp,
						PredMicroDown = microInfo.ConsiderDown,
						FactMicroUp = r.FactMicroUp,
						FactMicroDown = r.FactMicroDown,
						Entry = entry,
						MaxHigh24 = maxHigh,
						MinLow24 = minLow,
						Close24 = close24,
						RegimeDown = r.RegimeDown,
						Reason = reason,
						MinMove = r.MinMove
						});

					// ===== почасовой слой с фильтром =====
					if (sol1h != null && sol1h.Count > 0 && hasDir)
						{
						bool goLong = predClass == 2 || (predClass == 1 && microInfo.ConsiderUp);
						bool goShort = predClass == 0 || (predClass == 1 && microInfo.ConsiderDown);
						bool strong = predClass == 2 || predClass == 0;

						var hourOutcome = HourlyTradeEvaluator.EvaluateOne (
							sol1h,
							r.Date,
							goLong,
							goShort,
							entry,
							r.MinMove,
							strong
						);

						// нас интересуют только однозначные дни
						if (hourOutcome.Result == HourlyTradeResult.TpFirst ||
							hourOutcome.Result == HourlyTradeResult.SlFirst)
							{
							bool skip = false;
							if (slEngine != null)
								{
								var slFeats = SlFeatureBuilder.Build (
									r.Date,
									goLong,
									strong,
									r.MinMove,
									entry,
									sol1h
								);

								var predSl = slEngine.Predict (new SlHitSample
									{
									Label = false,
									Features = slFeats,
									EntryUtc = r.Date
									});

								// статистика
								slProbSum += predSl.Probability;
								slProbCount++;
								if (predSl.Probability > slProbMax) slProbMax = predSl.Probability;

								if (predSl.Probability >= SlFilterThreshold)
									{
									skip = true;
									filtSkipped++;
									}
								}

							if (!skip)
								{
								filtTrades++;
								double tradeRet = hourOutcome.Result == HourlyTradeResult.TpFirst
									? hourOutcome.TpPct
									: -hourOutcome.SlPct;

								filtEq *= (1.0 + tradeRet);
								if (filtEq > filtPeak) filtPeak = filtEq;
								double dd = (filtPeak - filtEq) / filtPeak;
								if (dd > filtMaxDd) filtMaxDd = dd;

								if (hourOutcome.Result == HourlyTradeResult.TpFirst) filtTp++;
								else filtSl++;
								}
							}
						}

					localTested++;
					}

				Console.WriteLine ($"[roll] base acc: {(localTested == 0 ? 0 : 100.0 * localBase / localTested):0.0}% ({localBase}/{localTested})");
				Console.WriteLine ($"[roll] micro-aware acc: {(localTested == 0 ? 0 : 100.0 * localMicro / localTested):0.0}% ({localMicro}/{localTested})");
				Console.WriteLine ($"[roll] tp-hit: {(localTpTrades == 0 ? 0 : 100.0 * localTpOk / localTpTrades):0.0}% ({localTpOk}/{localTpTrades})");

				// один последний день — как ты просил
				var lastDay = testRows.OrderByDescending (x => x.Date).First ();
					{
					var (predClass, _, reason, microInfo) = engine.Predict (lastDay);
					var fwd2 = BacktestHelpers.GetForwardInfo (lastDay.Date, sol6hDict);

					double rsi = lastDay.SolRsiCentered + 50.0;
					double atrPct = lastDay.AtrPct * 100.0;
					double minMovePct = lastDay.MinMove * 100.0;

					Console.WriteLine ($"[dbg-day] {lastDay.Date:yyyy-MM-dd HH:mm}");
					Console.WriteLine ($"  entry={fwd2.entry:0.####}  maxHigh24={fwd2.maxHigh:0.####}  minLow24={fwd2.minLow:0.####}  fwdClose24={fwd2.fwdClose:0.####}");
					Console.WriteLine ($"  rsi:{rsi:0.0}  atr:{atrPct:0.00}%  minMove:{minMovePct:0.00}%");
					Console.WriteLine ($"  Прогноз:{PrintHelpers.ClassToRu (predClass)}  Микро:{PrintHelpers.MicroToRu (microInfo)}  Факт:{PrintHelpers.FactToRu (lastDay)}  (reason:{reason})");
					}

				cursor = cursor.AddDays (RollingTestDays);
				if (cursor >= maxDate) break;
				}

			// ===== SUMMARY =====
			Console.WriteLine ();
			Console.WriteLine ("==== SUMMARY ====");
			Console.WriteLine ($"total tested: {allRecords.Count}");

			var cls = ClassificationMetrics.Compute (allRecords, useMicro: true);
			Console.WriteLine ("=== Classification (micro-aware from file) ===");
			Console.WriteLine ($"accuracy: {cls.Accuracy * 100:0.0}%");
			Console.WriteLine ($"macro F1: {cls.MacroF1 * 100:0.0}%, micro F1: {cls.MicroF1 * 100:0.0}%");
			Console.WriteLine ("class | support | precision% | recall% | f1%");
			foreach (var c in cls.PerClass.OrderBy (c => c.Label))
				{
				Console.WriteLine (
					$"{c.Label,5} | {c.Support,6} | {c.Precision * 100,9:0.0} | {c.Recall * 100,7:0.0} | {c.F1 * 100,4:0.0}"
				);
				}

			var len = ClassificationMetrics.ComputeLenient (allRecords);
			Console.WriteLine ();
			Console.WriteLine ("=== Lenient accuracy (direction-aware) ===");
			Console.WriteLine ($"lenient acc: {len.Accuracy * 100:0.0}% ({len.Correct}/{len.Total})");

			var tr = TradingMetrics.Compute (allRecords, TpPct);
			Console.WriteLine ();
			Console.WriteLine ($"=== Trading (tp-or-close, {TpPct * 100:0.#}%) ===");
			Console.WriteLine ($"Total PnL: {tr.TotalPnlPct:0.0}% (~x{tr.TotalPnlMultiplier:0.00})");
			Console.WriteLine ($"Max DD: {tr.MaxDrawdownPct:0.0}%");
			Console.WriteLine ($"Sharpe: {tr.Sharpe:0.##}");
			Console.WriteLine ($"Sortino: {tr.Sortino:0.##}");
			Console.WriteLine ($"Calmar: {tr.Calmar:0.##}");
			Console.WriteLine ($"Trades (opened): {tr.Trades}");
			Console.WriteLine ($"tp-hit overall: {(tr.TpTotal == 0 ? 0 : 100.0 * tr.TpHits / tr.TpTotal):0.0}% ({tr.TpHits}/{tr.TpTotal})");

			// ===== raw hourly =====
			if (sol1h != null && sol1h.Count > 0)
				{
				var hourly = HourlyTradeEvaluator.Evaluate (allRecords, sol1h);
				Console.WriteLine ();
				Console.WriteLine ("=== Trading WITH hourly TP/SL (raw) ===");
				Console.WriteLine ($"Total PnL: {hourly.TotalPnlPct:0.0}% (~x{hourly.TotalPnlMultiplier:0.00})");
				Console.WriteLine ($"Max DD: {hourly.MaxDrawdownPct:0.0}%");
				Console.WriteLine ($"Trades (opened): {hourly.Trades}");
				Console.WriteLine ($"tp-first: {hourly.TpFirst}");
				Console.WriteLine ($"sl-first: {hourly.SlFirst}");
				Console.WriteLine ($"ambiguous (tp & sl in 1h): {hourly.Ambiguous}");
				}
			else
				{
				Console.WriteLine ("[hourly] skipped: no 1h candles");
				}

			// ===== filtered hourly (онлайн) =====
			Console.WriteLine ();
			Console.WriteLine ("=== Trading WITH hourly TP/SL (filtered by online SL-model) ===");
			Console.WriteLine ($"Total PnL: {(filtEq - 1.0) * 100.0:0.0}% (~x{filtEq:0.00})");
			Console.WriteLine ($"Max DD: {filtMaxDd * 100.0:0.0}%");
			Console.WriteLine ($"Trades (opened): {filtTrades}");
			Console.WriteLine ($"tp-first: {filtTp}");
			Console.WriteLine ($"sl-first: {filtSl}");
			Console.WriteLine ($"skipped by sl-model: {filtSkipped}");

			Console.WriteLine ();
			Console.WriteLine ($"[sl-offline] total samples: {slOfflineSamples.Count}");
			if (slProbCount > 0)
				{
				Console.WriteLine ($"[sl-model] avg p(SL-first) on real entries: {slProbSum / slProbCount:0.000}");
				Console.WriteLine ($"[sl-model] max p(SL-first) on real entries: {slProbMax:0.000}");
				}
			else
				{
				Console.WriteLine ("[sl-model] no real entries were scored by SL-model");
				}
			}
		}
	}
