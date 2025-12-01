using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Daily;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Daily
	{
	/// <summary>
	/// E2E-тесты дневной модели:
	/// 1) монотонный тренд на синтетике — sanity, что пайплайн вообще работает;
	/// 2) зигзагообразная история — sanity, что модель не вырождается в константу по классам.
	///
	/// Важно:
	/// - пайплайн совпадает с боевым: RowBuilder + ModelTrainer + PredictionEngine;
	/// - Anti-D / PnL здесь не участвуют;
	/// - всё крутится только вокруг дневной разметки и обучения дневного бандла.
	/// </summary>
	public sealed class DailyModelEndToEndTests
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		/// <summary>
		/// Монотонный тренд на синтетике:
		/// - цены SOL/BTC/PAXG плавно растут;
		/// - 1m-серия тоже плавно растёт;
		/// - FNG/DXY константные.
		///
		/// Задача теста:
		/// - убедиться, что весь пайплайн отрабатывает без исключений;
		/// - PredictionEngine выдаёт предсказания на всех строках;
		/// - классы лежат в допустимом диапазоне [0..2].
		///
		/// Специально НЕ проверяем, что модель не константа:
		/// на таком тренде разметка может действительно выродиться,
		/// и это будет честное поведение таргета, а не баг.
		/// </summary>
		[Fact]
		public void DailyModel_EndToEnd_OnMonotonicTrendHistory_ProducesValidPredictions ()
			{
			// 1. Генерируем синтетическую трендовую историю.
			var history = BuildMonotonicTrendHistory ();

			// 2. Строим дневные строки через RowBuilder.
			var rows = RowBuilder.BuildRowsDaily (
				solWinTrain: history.SolAll6h,
				btcWinTrain: history.BtcAll6h,
				paxgWinTrain: history.PaxgAll6h,
				solAll6h: history.SolAll6h,
				solAll1m: history.SolAll1m,
				fngHistory: history.Fng,
				dxySeries: history.Dxy,
				extraDaily: null,
				nyTz: NyTz
			);

			Assert.NotEmpty (rows);

			var ordered = rows.OrderBy (r => r.Date).ToList ();
			Assert.True (ordered.Count > 150, "Для e2e-теста нужно достаточно дней истории.");

			// 3. Делим на train/OOS по дате и тренируем дневной бандл.
			const int HoldoutDays = 60;

			var result = TrainAndPredict (ordered, HoldoutDays);

			// 4. Базовые инварианты для трендового sanity-теста.
			Assert.True (result.TotalPredictions > 0, "PredictionEngine не выдал ни одного предсказания (trend).");
			Assert.Equal (0, result.OutOfRangeCount); // все классы в [0..2]

			Console.WriteLine (
				"[daily-e2e:trend] predicted classes = " +
				string.Join (", ", result.PredictedClassSet.OrderBy (x => x)));
			}

		/// <summary>
		/// Зигзагообразная история:
		/// - синтетика строится по дням, чередуя "сильный up" и "сильный down";
		/// - внутри дня 1m-цена монотонно идёт либо вверх, либо вниз примерно на ±10%;
		/// - 6h-свечи агрегируются из этих минуток.
		///
		/// Задача теста:
		/// - гарантировать, что при явно разнотипных дневных паттернах
		///   итоговая дневная модель не вырождается в одну константу по классам;
		/// - при этом всё так же идёт через реальный RowBuilder + ModelTrainer + PredictionEngine.
		/// </summary>
		[Fact]
		public void DailyModel_EndToEnd_OnZigZagHistory_UsesAtLeastTwoClasses ()
			{
			// 1. Зигзагообразная синтетика: up/down-дни чередуются.
			var history = BuildZigZagHistory ();

			// 2. Строим дневные строки.
			var rows = RowBuilder.BuildRowsDaily (
				solWinTrain: history.SolAll6h,
				btcWinTrain: history.BtcAll6h,
				paxgWinTrain: history.PaxgAll6h,
				solAll6h: history.SolAll6h,
				solAll1m: history.SolAll1m,
				fngHistory: history.Fng,
				dxySeries: history.Dxy,
				extraDaily: null,
				nyTz: NyTz
			);

			Assert.NotEmpty (rows);

			var ordered = rows.OrderBy (r => r.Date).ToList ();
			Assert.True (ordered.Count > 120, "Для зигзаг-теста нужно достаточно дней истории.");

			// 3. Делим на train/OOS и тренируем дневной бандл.
			const int HoldoutDays = 40;

			var result = TrainAndPredict (ordered, HoldoutDays);

			// 4. Инварианты:
			// - PredictionEngine хоть что-то предсказывает;
			// - все классы в диапазоне [0..2];
			// - на всей истории используется как минимум два разных класса.
			Assert.True (result.TotalPredictions > 0, "PredictionEngine не выдал ни одного предсказания (zigzag).");
			Assert.Equal (0, result.OutOfRangeCount);

			Assert.True (
				result.PredictedClassSet.Count >= 2,
				"Дневная модель выродилась в константу по классам на зигзагообразной истории."
			);

			Console.WriteLine (
				"[daily-e2e:zigzag] predicted classes = " +
				string.Join (", ", result.PredictedClassSet.OrderBy (x => x)));
			}

		// --------------------------------------------------------------------
		// ВСПОМОГАТЕЛЬНЫЕ СТРУКТУРЫ
		// --------------------------------------------------------------------

		/// <summary>
		/// Контейнер синтетической дневной истории:
		/// - 6h-ряды по SOL/BTC/PAXG;
		/// - 1m-ряд по SOL;
		/// - FNG/DXY в виде дневных словарей.
		/// </summary>
		private sealed class SyntheticDailyHistory
			{
			public List<Candle6h> SolAll6h { get; }
			public List<Candle6h> BtcAll6h { get; }
			public List<Candle6h> PaxgAll6h { get; }
			public List<Candle1m> SolAll1m { get; }
			public Dictionary<DateTime, int> Fng { get; }
			public Dictionary<DateTime, double> Dxy { get; }

			public SyntheticDailyHistory (
				List<Candle6h> solAll6h,
				List<Candle6h> btcAll6h,
				List<Candle6h> paxgAll6h,
				List<Candle1m> solAll1m,
				Dictionary<DateTime, int> fng,
				Dictionary<DateTime, double> dxy )
				{
				SolAll6h = solAll6h;
				BtcAll6h = btcAll6h;
				PaxgAll6h = paxgAll6h;
				SolAll1m = solAll1m;
				Fng = fng;
				Dxy = dxy;
				}
			}

		/// <summary>
		/// Результат тренировки и прогон PredictionEngine для удобства проверок.
		/// </summary>
		private sealed class PredictionRunResult
			{
			public int TotalPredictions { get; init; }
			public int OutOfRangeCount { get; init; }
			public HashSet<int> PredictedClassSet { get; init; } = new HashSet<int> ();
			}

		// --------------------------------------------------------------------
		// ПОМОЩНИК: ТРЕНИРОВКА И ПРОГОН ПРЕДИКТОРА
		// --------------------------------------------------------------------

		/// <summary>
		/// Общий helper:
		/// - делит ordered-ряды на train/OOS по последней дате и горизонту holdoutDays;
		/// - тренирует дневной бандл через ModelTrainer;
		/// - прогоняет PredictionEngine по всей истории;
		/// - собирает статистику по классам.
		///
		/// Вынесено в helper, чтобы не дублировать код в двух тестах.
		/// </summary>
		private static PredictionRunResult TrainAndPredict ( List<DataRow> orderedRows, int holdoutDays )
			{
			if (orderedRows == null) throw new ArgumentNullException (nameof (orderedRows));
			if (orderedRows.Count == 0) throw new ArgumentException ("rows must be non-empty", nameof (orderedRows));

			var maxDate = orderedRows.Last ().Date;
			var trainUntil = maxDate.AddDays (-holdoutDays);

			var trainRows = orderedRows
				.Where (r => r.Date <= trainUntil)
				.ToList ();

			var oosRows = orderedRows
				.Where (r => r.Date > trainUntil)
				.ToList ();

			// Минимальные требования к размерам выборок,
			// чтобы тест не проходил на "3 дня train / 2 дня OOS".
			Assert.True (trainRows.Count > 50, "Слишком мало train-дней для обучения.");
			Assert.True (oosRows.Count > 10, "Слишком мало OOS-дней для проверки.");

			// Тренируем дневной бандл.
			var trainer = new ModelTrainer ();
			var bundle = trainer.TrainAll (trainRows);

			Assert.NotNull (bundle);
			Assert.NotNull (bundle.MlCtx);
			Assert.NotNull (bundle.MoveModel);
			// Dir-модели могут быть опциональны — жёстко не проверяем.

			// Прогон PredictionEngine по всей истории (train+OOS).
			var engine = new PredictionEngine (bundle);

			int totalPredictions = 0;
			int clsOutOfRange = 0;
			var predictedClasses = new HashSet<int> ();

			foreach (var r in orderedRows)
				{
				var pred = engine.Predict (r);

				totalPredictions++;

				int cls = pred.Class;
				predictedClasses.Add (cls);

				if (cls < 0 || cls > 2)
					{
					clsOutOfRange++;
					}
				}

			return new PredictionRunResult
				{
				TotalPredictions = totalPredictions,
				OutOfRangeCount = clsOutOfRange,
				PredictedClassSet = predictedClasses
				};
			}

		// --------------------------------------------------------------------
		// СИНТЕТИЧЕСКАЯ ИСТОРИЯ: МОНОТОННЫЙ ТРЕНД
		// --------------------------------------------------------------------

		/// <summary>
		/// Строит простую трендовую историю:
		/// - 6h-цены SOL/BTC/PAXG линейно растут;
		/// - 1m-цена SOL тоже растёт, но с меньшим шагом;
		/// - FNG/DXY — константные.
		///
		/// Такой сценарий нужен как минимальный sanity-чек пайплайна:
		/// здесь не пытаемся силой выбить все классы, только проверяем валидность.
		/// </summary>
		private static SyntheticDailyHistory BuildMonotonicTrendHistory ()
			{
			const int total6h = 1000;
			var start = new DateTime (2020, 1, 1, 2, 0, 0, DateTimeKind.Utc);

			var solAll6h = new List<Candle6h> (total6h);
			var btcAll6h = new List<Candle6h> (total6h);
			var paxgAll6h = new List<Candle6h> (total6h);

			for (int i = 0; i < total6h; i++)
				{
				var t = start.AddHours (6 * i);

				double solPrice = 100.0 + i * 0.5;     // достаточно плавный, но заметный тренд
				double btcPrice = 50.0 + i * 0.25;
				double goldPrice = 1500.0 + i * 0.1;

				solAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Close = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0
					});

				btcAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Close = btcPrice,
					High = btcPrice + 1.0,
					Low = btcPrice - 1.0
					});

				paxgAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Close = goldPrice,
					High = goldPrice + 1.0,
					Low = goldPrice - 1.0
					});
				}

			// 1m-серия SOL: очень плавный рост.
			int totalMinutes = total6h * 6 * 60;
			var solAll1m = new List<Candle1m> (totalMinutes);

			for (int i = 0; i < totalMinutes; i++)
				{
				var t = start.AddMinutes (i);
				double price = 100.0 + i * 0.0002; // ~0.3% в день, сильно меньше 3%/5% порогов SL

				solAll1m.Add (new Candle1m
					{
					OpenTimeUtc = t,
					Close = price,
					High = price + 0.0005,
					Low = price - 0.0005
					});
				}

			// FNG/DXY: ровные ряды без пропусков.
			var fng = new Dictionary<DateTime, int> ();
			var dxy = new Dictionary<DateTime, double> ();

			// Берём запас по датам, чтобы точно покрыть диапазон RowBuilder.
			var firstDate = start.Date.AddDays (-120);
			var lastDate = start.Date.AddDays (400);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fng[key] = 50;
				dxy[key] = 100.0;
				}

			return new SyntheticDailyHistory (solAll6h, btcAll6h, paxgAll6h, solAll1m, fng, dxy);
			}

		// --------------------------------------------------------------------
		// СИНТЕТИЧЕСКАЯ ИСТОРИЯ: ЗИГЗАГ (ЧЕРЕДУЮЩИЕСЯ UP/DOWN-ДНИ)
		// --------------------------------------------------------------------

		/// <summary>
		/// Строит зигзагообразную историю:
		/// - каждый календарный день — либо "сильный up", либо "сильный down";
		/// - внутри дня цена SOL по 1m идёт примерно на ±10%;
		/// - 6h-свечи агрегируются из минуток;
		/// - BTC/PAXG строятся как простые линейные функции от SOL (для наличия кросс-активов).
		///
		/// Такой сценарий должен давать заведомо разнотипные path-label для дневной модели,
		/// поэтому вырождение предсказаний в одну константу здесь уже будет признаком проблемы.
		/// </summary>
		private static SyntheticDailyHistory BuildZigZagHistory ()
			{
			const int days = 240;                 // ~8 месяцев сигнальной истории
			const int minutesPerDay = 24 * 60;
			int totalMinutes = days * minutesPerDay;

			var start = new DateTime (2020, 1, 1, 2, 0, 0, DateTimeKind.Utc);

			var solAll1m = new List<Candle1m> (totalMinutes);
			var solAll6h = new List<Candle6h> ();
			var btcAll6h = new List<Candle6h> ();
			var paxgAll6h = new List<Candle6h> ();

			double currentPrice = 100.0;

			// Для агрегации 6h-свечей.
			const int minutesIn6h = 6 * 60;
			int minuteIn6h = 0;
			DateTime current6hOpenTime = start;
			double current6hHigh = currentPrice;
			double current6hLow = currentPrice;

			var t = start;

			for (int i = 0; i < totalMinutes; i++)
				{
				int dayIndex = i / minutesPerDay;
				int indexInDay = i % minutesPerDay;

				// Чередуем дни: 0,2,4,... — "up", 1,3,5,... — "down".
				bool isUpDay = (dayIndex % 2 == 0);

				// В начале дня фиксируем стартовую цену и целевую цену конца дня.
				if (indexInDay == 0)
					{
					// Старт дня — текущая цена.
					// Выбираем целевой дневной ход ±10% от цены открытия.
					double targetMovePct = isUpDay ? 0.10 : -0.10;
					double targetPriceEnd = currentPrice * (1.0 + targetMovePct);

					// Сохраняем в локальные переменные через замыкание на день.
					_dayStartPrice = currentPrice;
					_dayTargetPriceEnd = targetPriceEnd;
					}

				// Локальные переменные дня — формально static-поля для простоты, без аллокаций.
				double dayStartPrice = _dayStartPrice;
				double dayTargetPriceEnd = _dayTargetPriceEnd;

				// Линейная траектория внутри дня: от dayStartPrice к dayTargetPriceEnd.
				double alpha = (indexInDay + 1) / (double) minutesPerDay;
				double price = dayStartPrice + (dayTargetPriceEnd - dayStartPrice) * alpha;

				// 1m-свеча для SOL.
				double high1m = price * 1.001;
				double low1m = price * 0.999;

				var c1m = new Candle1m
					{
					OpenTimeUtc = t,
					Close = price,
					High = high1m,
					Low = low1m
					};

				solAll1m.Add (c1m);

				// Агрегируем 6h-свечи из минуток.
				if (minuteIn6h == 0)
					{
					current6hOpenTime = t;
					current6hHigh = high1m;
					current6hLow = low1m;
					}
				else
					{
					if (high1m > current6hHigh) current6hHigh = high1m;
					if (low1m < current6hLow) current6hLow = low1m;
					}

				minuteIn6h++;

				if (minuteIn6h == minutesIn6h)
					{
					// Закрываем 6h-свечу по текущей цене.
					var sol6h = new Candle6h
						{
						OpenTimeUtc = current6hOpenTime,
						Close = price,
						High = current6hHigh,
						Low = current6hLow
						};
					solAll6h.Add (sol6h);

					// Для BTC/PAXG берём простые линейные функции от SOL:
					// это даёт кросс-активы, но без сложной динамики.
					double btcPrice = price * 0.5;
					double paxgPrice = price * 10.0;

					btcAll6h.Add (new Candle6h
						{
						OpenTimeUtc = current6hOpenTime,
						Close = btcPrice,
						High = btcPrice * 1.001,
						Low = btcPrice * 0.999
						});

					paxgAll6h.Add (new Candle6h
						{
						OpenTimeUtc = current6hOpenTime,
						Close = paxgPrice,
						High = paxgPrice * 1.001,
						Low = paxgPrice * 0.999
						});

					minuteIn6h = 0;
					}

				// Переходим к следующей минуте.
				currentPrice = price;
				t = t.AddMinutes (1);
				}

			// FNG/DXY: лёгкая синтетика с минимальной вариацией,
			// чтобы соблюсти требования RowBuilder по покрытию.
			var fng = new Dictionary<DateTime, int> ();
			var dxy = new Dictionary<DateTime, double> ();

			var firstDate = start.Date.AddDays (-30);
			var lastDate = start.Date.AddDays (days + 30);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				// Лёгкие колебания, чтобы не было идеально ровного ряда.
				fng[key] = 40 + (d.Day % 20);           // 40..59
				dxy[key] = 95.0 + (d.Day % 10) * 0.1;   // 95.0..95.9
				}

			return new SyntheticDailyHistory (solAll6h, btcAll6h, paxgAll6h, solAll1m, fng, dxy);
			}

		// Локальные статические поля для хранения параметров дня внутри BuildZigZagHistory.
		// Это простой способ не создавать отдельный объект состояния на каждый день.
		private static double _dayStartPrice;
		private static double _dayTargetPriceEnd;
		}
	}
