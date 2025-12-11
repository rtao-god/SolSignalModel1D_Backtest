using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML;

using SolSignalModel1D_Backtest.Core.ML.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Daily
	{
	/// <summary>
	/// E2E-тесты для дневной модели:
	/// - синтетическая монотонная история (smoke: пайплайн не падает, классы валидные);
	/// - синтетическая зигзагообразная история (модель не должна вырождаться в константу).
	/// Пайплайн: RowBuilder.BuildRowsDaily → ModelTrainer.TrainAll → PredictionEngine.
	/// </summary>
	public sealed class DailyModelEndToEndTests
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		/// <summary>
		/// Монотонный ап-тренд:
		/// - проверяем, что весь пайплайн ходит от конца до конца;
		/// - PredictionEngine выдаёт хотя бы одно предсказание и все классы в диапазоне [0..2].
		/// В этом сценарии микро-слой может не обучиться (слишком мало микро-дней) — это допустимо.
		/// </summary>
		[Fact]
		public void DailyModel_EndToEnd_OnMonotonicTrendHistory_ProducesValidPredictions ()
			{
			var rows = BuildMonotonicHistory ();
			var ordered = rows.OrderBy (r => r.Date).ToList ();

			const int HoldoutDays = 60;
			var result = TrainAndPredict (ordered, HoldoutDays);

			Assert.True (result.TotalPredictions > 0, "PredictionEngine не выдал ни одного предсказания.");
			Assert.Equal (0, result.ClassesOutOfRange);
			Assert.InRange (result.PredictedClasses.Count, 1, 3);
			}

		/// <summary>
		/// Зигзагообразная история:
		/// - специально даём SOL волнообразный профиль по минуткам;
		/// - требуем, чтобы модель использовала минимум два класса на истории (не выродилась в константу).
		/// Микро-слой здесь также опционален, важен именно дневной Pred.Class.
		/// </summary>
		[Fact]
		public void DailyModel_EndToEnd_OnZigZagHistory_UsesAtLeastTwoClasses ()
			{
			var rows = BuildZigZagHistory ();
			var ordered = rows.OrderBy (r => r.Date).ToList ();

			const int HoldoutDays = 60;
			var result = TrainAndPredict (ordered, HoldoutDays);

			Assert.True (result.TotalPredictions > 0, "PredictionEngine не выдал ни одного предсказания.");
			Assert.Equal (0, result.ClassesOutOfRange);
			Assert.True (
				result.PredictedClasses.Count >= 2,
				"Дневная модель выродилась в константу по классам на зигзагообразной истории."
			);
			}

		private sealed class DailyE2eResult
			{
			public ModelBundle Bundle { get; init; } = null!;
			public int TotalPredictions { get; init; }
			public HashSet<int> PredictedClasses { get; init; } = new ();
			public int ClassesOutOfRange { get; init; }
			}

		/// <summary>
		/// Общая часть: делим на train/OOS, тренируем дневной бандл и прогоняем PredictionEngine по всей истории.
		/// </summary>
		private static DailyE2eResult TrainAndPredict ( List<DataRow> orderedRows, int holdoutDays )
			{
			if (orderedRows == null) throw new ArgumentNullException (nameof (orderedRows));
			Assert.NotEmpty (orderedRows);

			var minDate = orderedRows.First ().Date;
			var maxDate = orderedRows.Last ().Date;

			var trainUntil = maxDate.AddDays (-holdoutDays);

			var trainRows = orderedRows
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			var oosRows = orderedRows
				.Where (r => r.Date > trainUntil)
				.ToList ();

			Assert.True (trainRows.Count > 50,
				$"Слишком мало train-дней для обучения (train={trainRows.Count}, диапазон {minDate:yyyy-MM-dd}..{trainUntil:yyyy-MM-dd}).");
			Assert.True (oosRows.Count > 10,
				$"Слишком мало OOS-дней для проверки (oos={oosRows.Count}, диапазон {trainUntil:yyyy-MM-dd}..{maxDate:yyyy-MM-dd}).");

			// Обучаем дневной бандл.
			var trainer = new ModelTrainer ();
			var bundle = trainer.TrainAll (trainRows);

			Assert.NotNull (bundle);
			Assert.NotNull (bundle.MlCtx);
			Assert.NotNull (bundle.MoveModel);
			// Микро-слой здесь опционален: если микро-датасет мал, MicroFlatModel == null — это допустимо.
			// Любые реальные проблемы с микро-датасетом при нормальном объёме приведут к InvalidOperationException из MicroFlatTrainer.

			var engine = new PredictionEngine (bundle);

			int totalPredictions = 0;
			int clsOutOfRange = 0;
			var classes = new HashSet<int> ();

			foreach (var row in orderedRows)
				{
				var pred = engine.Predict (row);

				totalPredictions++;
				classes.Add (pred.Class);

				if (pred.Class < 0 || pred.Class > 2)
					clsOutOfRange++;
				}

			return new DailyE2eResult
				{
				Bundle = bundle,
				TotalPredictions = totalPredictions,
				PredictedClasses = classes,
				ClassesOutOfRange = clsOutOfRange
				};
			}

		/// <summary>
		/// Строит монотонную историю: плавный ап-тренд + лёгкий шум.
		/// Используется как smoke-тест пайплайна.
		/// </summary>
		private static List<DataRow> BuildMonotonicHistory ()
			{
			return BuildSyntheticRows (
				solPriceFunc: i =>
				{
					// Плавный рост + небольшой синусоидальный шум.
					double trend = 100.0 + 0.002 * i;          // ~0.2% на 100 минут
					double noise = Math.Sin (i * 0.005) * 0.3; // колебания порядка ±0.3$
					return trend + noise;
				}
			);
			}

		/// <summary>
		/// Строит зигзагообразную историю: выраженные волны вверх/вниз по SOL.
		/// </summary>
		private static List<DataRow> BuildZigZagHistory ()
			{
			return BuildSyntheticRows (
				solPriceFunc: i =>
				{
					// Крупные волны вверх/вниз + лёгкий ап-тренд,
					// чтобы по дневным окнам были как up-, так и down-дни.
					double basePrice = 100.0;
					double wave = 10.0 * Math.Sin (i * 0.01);   // период ~ 600 минут (~10 часов)
					double slowDrift = 0.0005 * i;              // лёгкий дрейф вверх
					return basePrice + wave + slowDrift;
				}
			);
			}

		/// <summary>
		/// Общий конструктор синтетической истории:
		/// - генерирует 1m-ряд по заданной функции цены SOL;
		/// - агрегирует его в 6h-свечи SOL;
		/// - строит простые тренды для BTC/PAXG;
		/// - генерирует FNG/DXY;
		/// - передаёт всё это в RowBuilder.BuildRowsDaily.
		/// </summary>
		private static List<DataRow> BuildSyntheticRows ( Func<int, double> solPriceFunc )
			{
			if (solPriceFunc == null) throw new ArgumentNullException (nameof (solPriceFunc));

			const int total6h = 1000;
			var start = new DateTime (2020, 1, 1, 2, 0, 0, DateTimeKind.Utc);

			int totalMinutes = total6h * 6 * 60;

			var solAll1m = new List<Candle1m> (totalMinutes);
			var solPrices = new double[totalMinutes];

			// 1. Минутный ряд SOL.
			for (int i = 0; i < totalMinutes; i++)
				{
				var t = start.AddMinutes (i);
				double price = solPriceFunc (i);

				if (price <= 0.0)
					price = 1.0; // защитный костыль, чтобы не словить нулевую/отрицательную цену в синтетике

				solPrices[i] = price;

				solAll1m.Add (new Candle1m
					{
					OpenTimeUtc = t,
					Close = price,
					High = price + 0.0005,
					Low = price - 0.0005
					});
				}

			// 2. Агрегация в 6h-свечи SOL + простые ряды BTC/PAXG.
			var solAll6h = new List<Candle6h> (total6h);
			var btcAll6h = new List<Candle6h> (total6h);
			var paxgAll6h = new List<Candle6h> (total6h);

			for (int block = 0; block < total6h; block++)
				{
				int startIdx = block * 360;
				int endIdx = startIdx + 360 - 1;

				double high = double.MinValue;
				double low = double.MaxValue;

				for (int idx = startIdx; idx <= endIdx; idx++)
					{
					double p = solPrices[idx];
					if (p > high) high = p;
					if (p < low) low = p;
					}

				double close = solPrices[endIdx];
				var t6 = start.AddHours (6 * block);

				solAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t6,
					Close = close,
					High = high,
					Low = low
					});

				// BTC/PAXG — простые плавные тренды, чтобы фичи были не константными.
				double btcPrice = 50.0 + 0.05 * block;
				double paxgPrice = 1500.0 + 0.02 * block;

				btcAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t6,
					Close = btcPrice,
					High = btcPrice + 1.0,
					Low = btcPrice - 1.0
					});

				paxgAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t6,
					Close = paxgPrice,
					High = paxgPrice + 1.0,
					Low = paxgPrice - 1.0
					});
				}

			// 3. FNG/DXY: простые плоские ряды, но с полным покрытием дат.
			var fng = new Dictionary<DateTime, double> ();
			var dxy = new Dictionary<DateTime, double> ();

			var firstDate = start.Date.AddDays (-200);
			var lastDate = start.Date.AddDays (400);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fng[key] = 50;
				dxy[key] = 100.0;
				}

			// 4. Строим дневные строки через реальный RowBuilder.
			var rows = RowBuilder.BuildRowsDaily (
				solWinTrain: solAll6h,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fng,
				dxySeries: dxy,
				extraDaily: null,
				nyTz: NyTz
			);

			Assert.NotEmpty (rows);
			return rows.OrderBy (r => r.Date).ToList ();
			}
		}
	}
