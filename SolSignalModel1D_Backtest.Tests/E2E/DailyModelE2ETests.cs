using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Time;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Tests.E2E
	{
	/// <summary>
	/// Сквозной тест дневной модели:
	/// RowBuilder + ModelTrainer + PredictionEngine на синтетическом "зигзаге".
	/// Цель:
	/// - таргеты на train не вырождаются в один класс;
	/// - предсказания PredLabel тоже не вырождаются в один класс.
	/// </summary>
	public sealed class DailyModelE2ETests
		{
		private static List<Candle6h> BuildZigZagSeries (
			int count,
			double startPrice,
			double upStepPct,
			double downStepPct,
			int seed )
			{
			var list = new List<Candle6h> (count);
			var t = new DateTime (2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			double price = startPrice;
			var rnd = new Random (seed);

			for (int i = 0; i < count; i++)
				{
				bool up = rnd.NextDouble () < 0.5;

				double newPrice = up
					? price * (1.0 + upStepPct)
					: price * (1.0 - downStepPct);

				double high = Math.Max (price, newPrice) * 1.002;
				double low = Math.Min (price, newPrice) * 0.998;

				list.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = price,
					High = high,
					Low = low,
					Close = newPrice
					});

				price = newPrice;
				t = t.AddHours (6);
				}

			return list;
			}

		private static List<Candle1m> BuildMinuteSeriesFrom6h ( IReadOnlyList<Candle6h> sixHours )
			{
			var list = new List<Candle1m> ();

			foreach (var c in sixHours)
				{
				double start = c.Open;
				double end = c.Close;
				var baseTime = c.OpenTimeUtc;

				for (int i = 0; i < 360; i++)
					{
					double alpha = (i + 1) / 360.0;
					double price = start + (end - start) * alpha;

					double high = price * 1.0008;
					double low = price * 0.9992;

					list.Add (new Candle1m
						{
						OpenTimeUtc = baseTime.AddMinutes (i),
						Open = price,
						High = high,
						Low = low,
						Close = price
						});
					}
				}

			return list;
			}

		[Fact]
		public void DailyModel_UsesMoreThanOneClass_OnSyntheticZigZag ()
			{
			const int total6h = 1600;     // ~400 дней
			const int holdoutDays = 60;  // OOS для проверки предиктов

			var nyTz = TimeZones.NewYork;

			var solAll6h = BuildZigZagSeries (total6h, 100.0, 0.015, 0.015, 42);
			var btcAll6h = BuildZigZagSeries (total6h, 50.0, 0.01, 0.01, 43);
			var paxgAll6h = BuildZigZagSeries (total6h, 1500.0, 0.004, 0.004, 44);

			var solAll1m = BuildMinuteSeriesFrom6h (solAll6h);

			var firstDate = solAll6h.First ().OpenTimeUtc.ToCausalDateUtc ().AddDays (-120);
			var lastDate = solAll6h.Last ().OpenTimeUtc.ToCausalDateUtc ().AddDays (120);

			var fng = new Dictionary<DateTime, double> ();
			var dxy = new Dictionary<DateTime, double> ();

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fng[key] = 50;
				dxy[key] = 100.0;
				}

			Dictionary<DateTime, (double Funding, double OI)>? extraDaily = null;

			var build = RowBuilder.BuildDailyRows (
				solWinTrain: solAll6h,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fng,
				dxySeries: dxy,
				extraDaily: extraDaily,
				nyTz: nyTz);

			var rows = build.LabeledRows;

			Assert.True (rows.Count > 200, $"Too few rows built for e2e test: {rows.Count}");

			static DateTime EntryUtc ( LabeledCausalRow r ) => r.EntryUtc.Value;

			var ordered = rows
				.OrderBy (EntryUtc)
				.ToList ();

			// TrueLabel не должен быть константой на синтетике.
			var labelHist = ordered
				.GroupBy (r => r.TrueLabel)
				.OrderBy (g => g.Key)
				.ToDictionary (g => g.Key, g => g.Count ());

			Assert.True (labelHist.Count >= 2,
				"Path-based TrueLabel on zigzag collapsed to a single class. This is suspicious for an e2e scenario.");

			foreach (var kv in labelHist)
				Assert.True (kv.Value >= 10, $"Label {kv.Key} has too few samples for training: {kv.Value}.");

			DateTime maxEntryUtc = EntryUtc (ordered.Last ());
			DateTime trainUntilEntryUtc = maxEntryUtc.AddDays (-holdoutDays);
			var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromExitDayKeyUtc (
				NyWindowing.ComputeExitDayKeyUtc (
					new EntryUtc (trainUntilEntryUtc),
					nyTz));

			var split = NyTrainSplit.SplitByBaselineExit (
				ordered: ordered,
				entrySelector: r => new EntryUtc (r.EntryUtc.Value),
				trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
				nyTz: nyTz);

			var trainRows = split.Train;

			Assert.True (trainRows.Count >= 100, $"Too few train rows for daily model e2e: {trainRows.Count}.");

			var trainer = new ModelTrainer ();
			var bundle = trainer.TrainAll (trainRows);

			Assert.NotNull (bundle);
			Assert.NotNull (bundle.MlCtx);
			Assert.NotNull (bundle.MoveModel);

			var engine = new PredictionEngine (bundle);

			var evalRows = split.Oos;

			if (evalRows.Count < 100)
				{
				evalRows = ordered
					.Skip (ordered.Count / 3)
					.Take (300)
					.ToList ();
				}

			Assert.NotEmpty (evalRows);

			var preds = new List<(int TrueLabel, int PredLabel)> (evalRows.Count);

			foreach (var r in evalRows)
				{
				var p = engine.PredictCausal (r.Causal);
				preds.Add ((r.TrueLabel, p.PredLabel));
				}

			var predHist = preds
				.GroupBy (p => p.PredLabel)
				.OrderBy (g => g.Key)
				.ToDictionary (g => g.Key, g => g.Count ());

			Assert.True (predHist.Count >= 2,
				"Daily model collapsed to a single predicted class on synthetic zigzag data.");

			double acc = preds.Count (p => p.TrueLabel == p.PredLabel) / (double) preds.Count;
			Assert.InRange (acc, 0.2, 0.95);
			}
		}
	}
