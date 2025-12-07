using Xunit;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML.Daily;
using SolSignalModel1D_Backtest.Core.ML.Shared;

namespace SolSignalModel1D_Backtest.Tests.E2E
	{
	/// <summary>
	/// Сквозной тест дневной модели:
	/// RowBuilder + ModelTrainer + PredictionEngine на синтетическом "зигзаге".
	/// Цель:
	/// - таргеты Label на train не вырождаются в один класс;
	/// - предсказания PredLabel тоже не вырождаются в один класс.
	/// Если где-то случится деградация (RowBuilder/Trainer/PredictionEngine),
	/// тест должен это поймать.
	/// </summary>
	public sealed class DailyModelE2ETests
		{
		/// <summary>
		/// Генерация зигзагообразного 6h-ряда:
		/// часть баров вверх, часть вниз, с фиксированным seed.
		/// Такой ряд даёт микс "up / down / flat" для path-таргетов.
		/// </summary>
		private static List<Candle6h> BuildZigZagSeries (
			int count,
			double startPrice,
			double upStepPct,
			double downStepPct,
			int seed )
			{
			var list = new List<Candle6h> (count);
			var t = new DateTime (2020, 1, 1, 2, 0, 0, DateTimeKind.Utc);
			double price = startPrice;
			var rnd = new Random (seed);

			for (int i = 0; i < count; i++)
				{
				bool up = rnd.NextDouble () < 0.5;

				double newPrice = up
					? price * (1.0 + upStepPct)
					: price * (1.0 - downStepPct);

				// Небольшой спред high/low, чтобы были адекватные диапазоны.
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

		/// <summary>
		/// Строит 1m-ряд на основе 6h-баров:
		/// внутри каждого 6h-интервала линейный переход от open к close.
		/// Это даёт согласованные минутки для PathLabeler/MinMove.
		/// </summary>
		private static List<Candle1m> BuildMinuteSeriesFrom6h ( IReadOnlyList<Candle6h> sixHours )
			{
			var list = new List<Candle1m> ();

			foreach (var c in sixHours)
				{
				double start = c.Open;
				double end = c.Close;
				var baseTime = c.OpenTimeUtc;

				// 6 часов = 360 минут.
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
			// Немного длиннее ряда, чтобы:
			// - индикаторы успели "разогреться";
			// - осталась нормальная train-часть + OOS.
			const int total6h = 800;       // ~200 дней
			const int holdoutDays = 60;    // OOS для проверки предиктов

			var nyTz = TimeZones.NewYork;

			// --- 1. Синтетические SOL/BTC/PAXG 6h-ряды (зигзаги с разными шагами) ---
			var solAll6h = BuildZigZagSeries (
				count: total6h,
				startPrice: 100.0,
				upStepPct: 0.015,
				downStepPct: 0.015,
				seed: 42);

			var btcAll6h = BuildZigZagSeries (
				count: total6h,
				startPrice: 50.0,
				upStepPct: 0.01,
				downStepPct: 0.01,
				seed: 43);

			var paxgAll6h = BuildZigZagSeries (
				count: total6h,
				startPrice: 1500.0,
				upStepPct: 0.004,
				downStepPct: 0.004,
				seed: 44);

			// Минутки для PathLabeler/MinMove.
			var solAll1m = BuildMinuteSeriesFrom6h (solAll6h);

			// --- 2. Макро-ряды FNG/DXY: ровные, без шума, только чтобы RowBuilder не падал ---
			var firstDate = solAll6h.First ().OpenTimeUtc.Date.AddDays (-120);
			var lastDate = solAll6h.Last ().OpenTimeUtc.Date.AddDays (120);

			var fng = new Dictionary<DateTime, double> ();
			var dxy = new Dictionary<DateTime, double> ();

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				// Важно: Kind = Utc, чтобы совпадать с логикой RowBuilder.
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fng[key] = 50;
				dxy[key] = 100.0;
				}

			Dictionary<DateTime, (double Funding, double OI)>? extraDaily = null;

			// --- 3. Строим DataRow через боевой RowBuilder ---
			var rows = RowBuilder.BuildRowsDaily (
				solWinTrain: solAll6h,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fng,
				dxySeries: dxy,
				extraDaily: extraDaily,
				nyTz: nyTz);

			Assert.True (rows.Count > 200, $"Too few rows built for e2e test: {rows.Count}");

			var ordered = rows
				.OrderBy (r => r.Date)
				.ToList ();

			// --- 4. Диагностика таргетов на трене: Label не должен быть константой ---
			var labelHist = ordered
				.GroupBy (r => r.Label)
				.OrderBy (g => g.Key)
				.ToDictionary (g => g.Key, g => g.Count ());

			// Должно быть хотя бы два класса.
			Assert.True (labelHist.Count >= 2,
				"Path-based Label на зигзаге выродился в один класс. Это странно для e2e-сценария.");

			// И каждый класс должен иметь хотя бы небольшое число примеров,
			// чтобы Trainer имел шанс чему-то научиться.
			foreach (var kv in labelHist)
				{
				Assert.True (kv.Value >= 10,
					$"Label {kv.Key} has too few samples for training: {kv.Value}.");
				}

			// --- 5. Делим на train / OOS, как в боевом коде, но с более коротким hold-out ---
			DateTime minDate = ordered.First ().Date;
			DateTime maxDate = ordered.Last ().Date;

			DateTime trainUntil = maxDate.AddDays (-holdoutDays);

			var trainRows = ordered
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			Assert.True (trainRows.Count >= 100,
				$"Too few train rows for daily model e2e: {trainRows.Count}.");

			// --- 6. Тренируем дневную модель через боевой ModelTrainer ---
			var trainer = new ModelTrainer ();
			var bundle = trainer.TrainAll (trainRows);

			Assert.NotNull (bundle);
			Assert.NotNull (bundle.MlCtx);
			Assert.NotNull (bundle.MoveModel);

			var engine = new PredictionEngine (bundle);

			// --- 7. Гоним предсказания по OOS-части и смотрим распределение PredLabel ---
			var evalRows = ordered
				.Where (r => r.Date > trainUntil)
				.ToList ();

			// На всякий случай, если OOS короткий, расширяем до середины истории.
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
				var p = engine.Predict (r);
				preds.Add ((r.Label, p.Class));
				}

			Assert.NotEmpty (preds);

			var predHist = preds
				.GroupBy (p => p.PredLabel)
				.OrderBy (g => g.Key)
				.ToDictionary (g => g.Key, g => g.Count ());

			// Ключевой инвариант e2e:
			// дневная модель НЕ должна схлопываться в один PredLabel на разумной синтетике.
			Assert.True (predHist.Count >= 2,
				"Daily model collapsed to a single predicted class on synthetic zigzag data.");

			// Дополнительная sanity-проверка: accuracy не должна быть ни нулём, ни магическими 100%.
			double acc = preds.Count (p => p.TrueLabel == p.PredLabel) / (double) preds.Count;
			Assert.InRange (acc, 0.2, 0.95);
			}
		}
	}
