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
		private const int RollingTrainDays = 240;
		private const int RollingTestDays = 60;

		public static async Task Main ( string[] args )
			{
			Console.OutputEncoding = System.Text.Encoding.UTF8;
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

			Console.WriteLine ("[init] start SOL daily backtest (rolling, short train)");

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

				// 1. собрали даты теста
				var testDates = testRows.Select (r => r.Date).ToHashSet ();

				// 2. треним ДВУХШАГОВЫЙ
				var trainer = new TwoStageModelTrainer ();
				var bundle = trainer.TrainAll (rows, testDates);

				// 3. предиктор
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

				// новые счётчики по режимам
				int hard0 = 0;
				int hard1 = 0;
				int hard2 = 0;

				foreach (var r in testRows)
					{
					var (predClass, probs, reason, microInfo) = engine.Predict (r);

					switch (r.HardRegime)
						{
						case 0: hard0++; break;
						case 1: hard1++; break;
						case 2: hard2++; break;
						}

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
				Console.WriteLine ($"[roll] hard regimes: 0={hard0}, 1={hard1}, 2={hard2}");
				Console.WriteLine ($"[roll] base={localBase}/{localTested} ({100.0 * localBase / localTested:0.0}%), micro={localMicro}/{localTested} ({100.0 * localMicro / localTested:0.0}%), hard: 0={hard0},1={hard1},2={hard2}");

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
		}
	}
