using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core;

namespace SolSignalModel1D_Backtest
	{
	internal class Program
		{
		private const int RollingTrainDays = 260;
		private const int RollingTestDays = 60;

		public static async Task Main ( string[] args )
			{
			Console.OutputEncoding = System.Text.Encoding.UTF8;
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

			Console.WriteLine ("[init] start SOL daily backtest (rolling, 2stage)");

			var http = HttpFactory.CreateDefault ("SolSignalModel1D_Backtest/1.0");

			Console.WriteLine ("[binance] load 6h SOL...");
			var sol6h = await DataLoading.GetBinance6h (http, "SOLUSDT", 6000);

			Console.WriteLine ("[binance] load 6h BTC...");
			var btc6h = await DataLoading.GetBinance6h (http, "BTCUSDT", 6000);

			Console.WriteLine ("[binance] load 6h PAXG (opt)...");
			var paxg6h = await DataLoading.GetBinance6h (http, "PAXGUSDT", 6000, allowNull: true);

			if (sol6h.Count == 0 || btc6h.Count == 0)
				{
				Console.WriteLine ("[fatal] no candles");
				return;
				}

			// удобно иметь словарь по времени для форварда
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

			// build rows
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

			int totalTested = 0;
			int totalCorrectBase = 0;
			int totalCorrectMicro = 0;
			double totalWeighted = 0.0;
			double totalBrier = 0.0;
			int totalMicroPred = 0;
			int totalMicroCorrect = 0;

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

				// список дат теста для тренера (чтобы не утянуть их в train)
				var testDates = new HashSet<DateTime> (testRows.Select (r => r.Date));

				var trainer = new TwoStageModelTrainer ();
				var bundle = trainer.TrainAll (rows, testDates);
				var engine = new PredictionEngine (bundle);

				Console.WriteLine ();
				Console.WriteLine ($"[roll] train: {trainStart:yyyy-MM-dd} .. {trainEnd:yyyy-MM-dd} ({trainRows.Count})");
				Console.WriteLine ($"[roll] test : {trainEnd:yyyy-MM-dd} .. {testEnd:yyyy-MM-dd} ({testRows.Count})");

				int localTested = 0;
				int localBase = 0;
				int localMicro = 0;
				double localWeighted = 0.0;
				double localBrier = 0.0;
				int localMicroPred = 0;
				int localMicroOk = 0;

				foreach (var r in testRows)
					{
					var (predClass, probs, reason, microInfo) = engine.Predict (r);

					bool baseCorrect = predClass == r.Label;
					if (baseCorrect) localBase++;

					bool microCorrect = engine.EvalMicroAware (r, predClass, microInfo);
					if (microCorrect) localMicro++;

					if (microInfo.Predicted)
						{
						localMicroPred++;
						if (microInfo.Correct) localMicroOk++;
						}

					localWeighted += engine.EvalWeighted (r, predClass, microInfo);

					// brier
					double[] yTrue = new double[3];
					yTrue[r.Label] = 1.0;
					double b = 0.0;
					for (int i = 0; i < 3; i++)
						{
						double diff = probs[i] - yTrue[i];
						b += diff * diff;
						}
					localBrier += b;

					localTested++;
					}

				Console.WriteLine ($"[roll] base acc: {(localTested == 0 ? 0 : 100.0 * localBase / localTested):0.0}% ({localBase}/{localTested})");
				Console.WriteLine ($"[roll] micro acc: {(localTested == 0 ? 0 : 100.0 * localMicro / localTested):0.0}% ({localMicro}/{localTested})");

				// вот тут печатаем 3 последнии даты этого окна
				var last3 = testRows.OrderByDescending (r => r.Date).Take (3).ToList ();
				foreach (var r in last3)
					{
					var (predClass, probs, reason, microInfo) = engine.Predict (r);
					var fwdInfo = GetForwardInfo (r.Date, sol6hDict);

					// восстановим реальный RSI: мы хранили "отцентрированный"
					double rsi = r.SolRsiCentered + 50.0;
					double atrPct = r.AtrPct * 100.0;
					double minMovePct = r.MinMove * 100.0;

					string predStr = ClassToRu (predClass);
					string microStr = MicroToRu (microInfo);
					string factStr = FactToRu (r);

					Console.WriteLine ($"[dbg-day] {r.Date:yyyy-MM-dd HH:mm}");
					Console.WriteLine ($"  entry={fwdInfo.entry:0.####}  maxHigh24={fwdInfo.maxHigh:0.####}  minLow24={fwdInfo.minLow:0.####}  fwdClose24={fwdInfo.fwdClose:0.####}");
					Console.WriteLine ($"  rsi:{rsi:0.0}  atr:{atrPct:0.00}%  minMove:{minMovePct:0.00}%");
					Console.WriteLine ($"  Прогноз:{predStr}  Микро:{microStr}  Факт:{factStr}  (reason:{reason})");
					}

				totalTested += localTested;
				totalCorrectBase += localBase;
				totalCorrectMicro += localMicro;
				totalWeighted += localWeighted;
				totalBrier += localBrier;
				totalMicroPred += localMicroPred;
				totalMicroCorrect += localMicroOk;

				cursor = cursor.AddDays (RollingTestDays);
				if (cursor >= maxDate) break;
				}

			Console.WriteLine ();
			Console.WriteLine ("==== ROLLING SUMMARY ====");
			Console.WriteLine ($"total tested: {totalTested}");
			Console.WriteLine ($"base acc: {(totalTested == 0 ? 0 : 100.0 * totalCorrectBase / totalTested):0.0}% ({totalCorrectBase}/{totalTested})");
			Console.WriteLine ($"micro acc: {(totalTested == 0 ? 0 : 100.0 * totalCorrectMicro / totalTested):0.0}% ({totalCorrectMicro}/{totalTested})");
			Console.WriteLine ($"weighted: {(totalTested == 0 ? 0 : totalWeighted / totalTested):0.00}");
			Console.WriteLine ($"brier: {(totalTested == 0 ? 0 : totalBrier / totalTested):0.000}");
			if (totalMicroPred > 0)
				Console.WriteLine ($"micro-only: {totalMicroPred}, correct {totalMicroCorrect} ({100.0 * totalMicroCorrect / totalMicroPred:0.0}%)");
			else
				Console.WriteLine ("micro-only: 0");
			}

		/// <summary>
		/// даём на вход время утреннего окна (6h-свечи), возвращаем:
		/// цену входа, максимум high за 24ч, минимум low за 24ч и close через 24ч
		/// </summary>
		private static (double entry, double maxHigh, double minLow, double fwdClose) GetForwardInfo (
			DateTime openUtc,
			Dictionary<DateTime, Candle6h> sol6hDict )
			{
			// текущая свеча
			if (!sol6hDict.TryGetValue (openUtc, out var cur))
				return (0, 0, 0, 0);

			double entry = cur.Close;
			double maxHigh = cur.High;
			double minLow = cur.Low;
			double fwdClose = cur.Close;

			// 4 свечи вперёд = 24 часа
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
			// ты просил camelCase именно вот так писать — сделаем как ты писал: БоковикРост / БоковикОбвал
			if (!m.Predicted && !m.ConsiderUp && !m.ConsiderDown)
				return "нет";

			if (m.ConsiderUp)
				return "БоковикРост";
			if (m.ConsiderDown)
				return "БоковикОбвал";

			return "нет";
			}

		private static string FactToRu ( DataRow r )
			{
			// если день реально был боковиком — уточним наклон
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
