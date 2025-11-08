using SolSignalModel1D_Backtest.Core;
using SolSignalModel1D_Backtest.Core.Analytics;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.Trading;
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
		private const double TpPct = 0.03; // для обычного "tp-or-close" дневного теста

		public static async Task Main ( string[] args )
			{
			Console.OutputEncoding = System.Text.Encoding.UTF8;
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

			Console.WriteLine ("[init] start SOL daily backtest (DETAILED, hourly TP/SL)");

			var http = HttpFactory.CreateDefault ("SolSignalModel1D_Backtest/1.0");

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

			var solTrainWindows = Windowing.FilterNyTrainWindows (sol6h, nyTz);
			var solMorningWindows = Windowing.FilterNyMorningOnly (sol6h, nyTz);
			var btcTrainWindows = Windowing.FilterNyTrainWindows (btc6h, nyTz);
			var paxgTrainWindows = paxg6h != null ? Windowing.FilterNyTrainWindows (paxg6h, nyTz) : new List<Candle6h> ();

			Console.WriteLine ($"[windows] SOL NY-окна (train: утро+день): {solTrainWindows.Count}");
			Console.WriteLine ($"[windows] SOL NY-окна (test: только утро): {solMorningWindows.Count}");

			Console.WriteLine ("[fng] load Fear & Greed");
			var fngHistory = await DataLoading.GetFngHistory (http);

			Console.WriteLine ("[dxy] load DXY proxy");
			DateTime oldest = solTrainWindows.First ().OpenTimeUtc.Date.AddDays (-45);
			DateTime newest = solTrainWindows.Last ().OpenTimeUtc.Date;
			var dxySeries = await DataLoading.GetDxySeries (http, oldest, newest);

			var extraDaily = DataLoading.TryLoadExtraDaily ("extra.json");

			// строим датасет
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

			var allMorning = rows.Where (r => r.IsMorning).OrderBy (r => r.Date).ToList ();
			if (allMorning.Count == 0)
				{
				Console.WriteLine ("[warn] no morning rows");
				return;
				}

			DateTime minDate = allMorning.First ().Date;
			DateTime maxDate = allMorning.Last ().Date;

			Console.WriteLine ();
			Console.WriteLine ("==== ROLLING ====");

			var allRecords = new List<PredictionRecord> ();

			DateTime cursor = minDate.AddDays (RollingTrainDays);
			while (true)
				{
				DateTime trainStart = cursor.AddDays (-RollingTrainDays);
				DateTime trainEnd = cursor;
				DateTime testEnd = cursor.AddDays (RollingTestDays);

				var trainRows = rows.Where (r => r.Date >= trainStart && r.Date < trainEnd).ToList ();
				var testRows = rows.Where (r => r.IsMorning && r.Date >= trainEnd && r.Date < testEnd).ToList ();

				if (testRows.Count == 0)
					{
					cursor = cursor.AddDays (RollingTestDays);
					if (cursor >= maxDate) break;
					continue;
					}

				var testDates = new HashSet<DateTime> (testRows.Select (r => r.Date));

				var trainer = new ModelTrainer ();
				var bundle = trainer.TrainAll (rows, testDates);
				var engine = new PredictionEngine (bundle);

				Console.WriteLine ();
				Console.WriteLine ($"[roll] train: {trainStart:yyyy-MM-dd} .. {trainEnd:yyyy-MM-dd} ({trainRows.Count})");
				Console.WriteLine ($"[roll] test : {trainEnd:yyyy-MM-dd} .. {testEnd:yyyy-MM-dd} ({testRows.Count})");

				int localTested = 0;
				int localBase = 0;
				int localMicro = 0;
				int localTpTrades = 0;
				int localTpOk = 0;
				double localPnlSum = 0.0;

				foreach (var r in testRows)
					{
					var (predClass, probs, reason, microInfo) = engine.Predict (r);

					var fwdInfo = GetForwardInfo (r.Date, sol6hDict);
					double entry = fwdInfo.entry;
					double close24 = fwdInfo.fwdClose;
					double maxHigh = fwdInfo.maxHigh;
					double minLow = fwdInfo.minLow;

					// дневной tp-or-close (старый расчёт)
					bool hasDir =
						predClass == 2 ||
						predClass == 0 ||
						(predClass == 1 && (microInfo.ConsiderUp || microInfo.ConsiderDown));

					if (hasDir)
						localTpTrades++;

					bool tpHit = false;
					double dealPnl = 0.0;

					if (hasDir)
						{
						if (predClass == 2 || (predClass == 1 && microInfo.ConsiderUp))
							{
							double tpPrice = entry * (1.0 + TpPct);
							if (maxHigh >= tpPrice)
								{
								tpHit = true;
								dealPnl = TpPct;
								}
							else
								{
								dealPnl = (close24 - entry) / entry;
								}
							}
						else
							{
							double tpPrice = entry * (1.0 - TpPct);
							if (minLow <= tpPrice)
								{
								tpHit = true;
								dealPnl = TpPct;
								}
							else
								{
								dealPnl = (entry - close24) / entry;
								}
							}

						if (tpHit) localTpOk++;
						localPnlSum += dealPnl;
						}

					bool baseCorrect = predClass == r.Label;
					if (baseCorrect) localBase++;

					bool microCorrect = engine.EvalMicroAware (r, predClass, microInfo);
					if (microCorrect) localMicro++;

					// записываем ПОЛНЫЙ рекорд — тут важно добавить MinMove
					allRecords.Add (new PredictionRecord
						{
						DateUtc = r.Date,
						TrueLabel = r.Label,
						PredLabel = predClass,
						PredMicroUp = microInfo.ConsiderUp,
						PredMicroDown = microInfo.ConsiderDown,
						FactMicroUp = r.FactMicroUp,
						FactMicroDown = r.FactMicroDown,
						Entry = fwdInfo.entry,
						MaxHigh24 = fwdInfo.maxHigh,
						MinLow24 = fwdInfo.minLow,
						Close24 = fwdInfo.fwdClose,
						RegimeDown = r.RegimeDown,
						Reason = reason,
						MinMove = r.MinMove   // ВОТ ЭТО — чтобы часовой умеял ставить адаптивный SL/TP
						});

					localTested++;
					}

				Console.WriteLine ($"[roll] base acc: {(localTested == 0 ? 0 : 100.0 * localBase / localTested):0.0}% ({localBase}/{localTested})");
				Console.WriteLine ($"[roll] micro-aware acc: {(localTested == 0 ? 0 : 100.0 * localMicro / localTested):0.0}% ({localMicro}/{localTested})");
				Console.WriteLine ($"[roll] tp-hit: {(localTpTrades == 0 ? 0 : 100.0 * localTpOk / localTpTrades):0.0}% ({localTpOk}/{localTpTrades})");

				// три последние даты
				var last3 = testRows.OrderByDescending (r => r.Date).Take (3).ToList ();
				foreach (var r in last3)
					{
					var (predClass, probs, reason, microInfo) = engine.Predict (r);
					var fwdInfo = GetForwardInfo (r.Date, sol6hDict);

					double rsi = r.SolRsiCentered + 50.0;
					double atrPct = r.AtrPct * 100.0;
					double minMovePct = r.MinMove * 100.0;

					Console.WriteLine ($"[dbg-day] {r.Date:yyyy-MM-dd HH:mm}");
					Console.WriteLine ($"  entry={fwdInfo.entry:0.####}  maxHigh24={fwdInfo.maxHigh:0.####}  minLow24={fwdInfo.minLow:0.####}  fwdClose24={fwdInfo.fwdClose:0.####}");
					Console.WriteLine ($"  rsi:{rsi:0.0}  atr:{atrPct:0.00}%  minMove:{minMovePct:0.00}%");
					Console.WriteLine ($"  Прогноз:{ClassToRu (predClass)}  Микро:{MicroToRu (microInfo)}  Факт:{FactToRu (r)}  (reason:{reason})");
					}

				cursor = cursor.AddDays (RollingTestDays);
				if (cursor >= maxDate) break;
				}

			// ===== финальный отчёт =====
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
				Console.WriteLine ($"{c.Label,5} | {c.Support,6} | {c.Precision * 100,9:0.0} | {c.Recall * 100,7:0.0} | {c.F1 * 100,4:0.0}");
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

			// ===== ЧАСОВОЙ адаптивный TP/SL =====
			var hourly = HourlyTradeEvaluator.Evaluate (allRecords, sol1h);
			Console.WriteLine ();
			Console.WriteLine ("=== Trading WITH hourly TP/SL (adaptive from MinMove) ===");
			Console.WriteLine ($"Total PnL: {hourly.TotalPnlPct:0.0}% (~x{hourly.TotalPnlMultiplier:0.00})");
			Console.WriteLine ($"Max DD: {hourly.MaxDrawdownPct:0.0}%");
			Console.WriteLine ($"Trades (opened): {hourly.Trades}");
			Console.WriteLine ($"tp-first: {hourly.TpFirst}");
			Console.WriteLine ($"sl-first: {hourly.SlFirst}");
			Console.WriteLine ($"ambiguous (tp & sl in 1h): {hourly.Ambiguous}");
			}

		private static (double entry, double maxHigh, double minLow, double fwdClose) GetForwardInfo (
			DateTime openUtc,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			if (!sol6hDict.TryGetValue (openUtc, out var cur))
				return (0, 0, 0, 0);

			double entry = cur.Close;
			double maxHigh = cur.High;
			double minLow = cur.Low;
			double fwdClose = cur.Close;

			for (int i = 1; i <= 4; i++)
				{
				var t = openUtc.AddHours (6 * i);
				if (!sol6hDict.TryGetValue (t, out var nxt))
					break;

				if (nxt.High > maxHigh) maxHigh = nxt.High;
				if (nxt.Low < minLow) minLow = nxt.Low;
				fwdClose = nxt.Close;
				}

			return (entry, maxHigh, minLow, fwdClose);
			}

		private static string ClassToRu ( int cls )
			{
			return cls switch
				{
					0 => "Обвал",
					1 => "Боковик",
					2 => "Рост",
					_ => "UNKNOWN"
					};
			}

		private static string MicroToRu ( MicroInfo m )
			{
			if (!m.Predicted && !m.ConsiderUp && !m.ConsiderDown)
				return "нет";

			if (m.ConsiderUp) return "БоковикРост";
			if (m.ConsiderDown) return "БоковикОбвал";
			return "нет";
			}

		private static string FactToRu (DataRow r )
			{
			if (r.Label == 1)
				{
				if (r.FactMicroUp) return "БоковикРост";
				if (r.FactMicroDown) return "БоковикОбвал";
				return "Боковик";
				}
			if (r.Label == 0) return "Обвал";
			if (r.Label == 2) return "Рост";
			return "UNKNOWN";
			}
		}
	}
