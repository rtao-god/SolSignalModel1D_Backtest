using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SolSignalModel1D_Backtest.Core.Analytics.Labeling;
using SolSignalModel1D_Backtest.Core.Analytics.MinMove;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Causal.ML.Delayed;

namespace SolSignalModel1D_Backtest.Tests.Leakage
	{
	/// <summary>
	/// Набор низкоуровневых тестов, которые проверяют:
	/// 1) PathLabeler не использует минутки после baseline-выхода.
	/// 2) MinMoveEngine игнорирует будущие дни в historyRows.
	/// 3) TargetLevelFeatureBuilder смотрит только на 1h до entryUtc.
	/// 4) MinuteDelayedEntryEvaluator смотрит только на минутки до baseline-выхода.
	///
	/// Эти тесты реально ловят утечки, если кто-то позже "подцепит" будущие данные.
	/// </summary>
	public class LeakageLowLevelTests
		{
		// ========== 1. PathLabeler: минутки после baseline-выхода не должны влиять на label ==========

		[Fact]
		public void PathLabeler_Ignores_Minutes_After_Baseline_Exit ()
			{
			// Берём произвольную entry-дату: пусть это будет понедельник 01:00 UTC.
			// Важно только, что Windowing.ComputeBaselineExitUtc вернёт корректный horizon.
			var nyTz = TimeZones.NewYork;
			var nyLocal = new DateTime (2025, 1, 6, 8, 0, 0, DateTimeKind.Unspecified); // Monday
			var entryUtc = TimeZoneInfo.ConvertTimeToUtc (nyLocal, nyTz);
			var endUtc = Windowing.ComputeBaselineExitUtc (entryUtc, nyTz);

			double entryPrice = 100.0;
			double minMove = 0.02;

			// Строим минутки от entry до endUtc + ещё 6 часов "будущего".
			var minutesBase = BuildMinuteCandles (entryUtc, endUtc.AddHours (6), entryPrice, 0.0005);

			// Копия, в которой после baseline-выхода рисуем дикие шпильки (утечки быть не должно).
			var minutesMutated = minutesBase
				.Select (c => new Candle1m
					{
					OpenTimeUtc = c.OpenTimeUtc,
					Open = c.Open,
					High = c.High,
					Low = c.Low,
					Close = c.Close,
					})
				.ToList ();

			foreach (var m in minutesMutated.Where (m => m.OpenTimeUtc >= endUtc))
				{
				// Делаем огромный выстрел вверх после baseline-выхода
				m.High *= 1.50;
				m.Low *= 0.50;
				m.Close *= 1.20;
				}

			// Вызываем PathLabeler для обоих наборов
			var label1 = PathLabeler.AssignLabel (
				entryUtc,
				entryPrice,
				minMove,
				minutesBase,
				out var firstDir1,
				out var firstTime1,
				out var up1,
				out var down1);

			var label2 = PathLabeler.AssignLabel (
				entryUtc,
				entryPrice,
				minMove,
				minutesMutated,
				out var firstDir2,
				out var firstTime2,
				out var up2,
				out var down2);

			// Если PathLabeler вдруг начнёт смотреть после endUtc, эти значения разъедутся.
			Assert.Equal (label1, label2);
			Assert.Equal (firstDir1, firstDir2);
			Assert.Equal (firstTime1, firstTime2);
			Assert.Equal (up1, up2, 10);    // допускаем минимальные double-расхождения
			Assert.Equal (down1, down2, 10);
			}

		// ========== 2. MinMoveEngine: future-rows не должны влиять на MinMove на asOfUtc ==========

		[Fact]
		public void MinMoveEngine_Uses_Only_Past_HistoryRows ()
			{
			// asOfUtc — "текущий" день, под который считаем minMove.
			var asOfUtc = new DateTime (2025, 3, 15, 0, 0, 0, DateTimeKind.Utc);

			// Базовая история до asOfUtc включительно (30 дней).
			var baseHistory = new List<DataRow> ();
			var start = asOfUtc.AddDays (-30);
			var rand = new Random (42);

			for (int i = 0; i <= 30; i++)
				{
				var date = start.AddDays (i);
				baseHistory.Add (new DataRow
					{
					Date = date,
					PathReachedUpPct = 0.02 + rand.NextDouble () * 0.03,      // 2–5%
					PathReachedDownPct = -(0.02 + rand.NextDouble () * 0.03)  // -2..-5%
					});
				}

			// Делаем "будущие" дни после asOfUtc с нереалистичными амплитудами (если MinMoveEngine их увидит — minMove изменится).
			var historyWithFuture = new List<DataRow> (baseHistory);
			for (int i = 1; i <= 10; i++)
				{
				var futureDate = asOfUtc.AddDays (i);
				historyWithFuture.Add (new DataRow
					{
					Date = futureDate,
					// Дикие амплитуды, которые должны бы сильно поднять любой квантиль, если вдруг будут учтены
					PathReachedUpPct = 0.50,
					PathReachedDownPct = -0.50
					});
				}

			// Конфиг и состояние одинаковые для обоих прогонов.
			var cfg = new MinMoveConfig
				{
				QuantileStart = 0.5,
				QuantileLow = 0.2,
				QuantileHigh = 0.9,
				QuantileWindowDays = 30,
				QuantileRetuneEveryDays = 5,
				AtrWeight = 0.5,
				DynVolWeight = 0.5,
				MinFloorPct = 0.01,
				MinCeilPct = 0.10,
				RegimeDownMul = 1.5
				};

			var state1 = new MinMoveState
				{
				EwmaVol = 0.0,
				QuantileQ = 0.0,
				LastQuantileTune = DateTime.MinValue
				};

			var state2 = new MinMoveState
				{
				EwmaVol = 0.0,
				QuantileQ = 0.0,
				LastQuantileTune = DateTime.MinValue
				};

			// atr/dynVol берём любые адекватные
			double atrPct = 0.03;
			double dynVol = 0.025;

			var r1 = MinMoveEngine.ComputeAdaptive (
				asOfUtc: asOfUtc,
				regimeDown: false,
				atrPct: atrPct,
				dynVol: dynVol,
				historyRows: baseHistory,
				cfg: cfg,
				state: state1);

			var r2 = MinMoveEngine.ComputeAdaptive (
				asOfUtc: asOfUtc,
				regimeDown: false,
				atrPct: atrPct,
				dynVol: dynVol,
				historyRows: historyWithFuture,
				cfg: cfg,
				state: state2);

			// Если MinMoveEngine вдруг начнёт смотреть на future-rows (Date >= asOfUtc),
			// r2.MinMove будет сильно отличаться от r1.MinMove.
			Assert.Equal (r1.MinMove, r2.MinMove, 6);
			Assert.Equal (r1.QuantileUsed, r2.QuantileUsed, 6);
			Assert.Equal (r1.EwmaVol, r2.EwmaVol, 6);
			}

		// ========== 3. TargetLevelFeatureBuilder: только 1h до entryUtc ==========

		[Fact]
		public void TargetLevelFeatureBuilder_Ignores_1h_Bars_After_Entry ()
			{
			var entryUtc = new DateTime (2025, 4, 10, 12, 0, 0, DateTimeKind.Utc);
			double entryPrice = 100.0;
			double dayMinMove = 0.03;
			bool goLong = true;
			bool strongSignal = true;

			// Строим 12 часов 1h-свечей: 6 часов до entry и 6 часов после.
			var startUtc = entryUtc.AddHours (-6);
			var candlesAll = BuildHourlyCandles (startUtc, hours: 12, basePrice: entryPrice, stepPerHour: 0.002);

			// Отдельный список только "до entry" — то, что builder должен видеть по определению.
			var candlesBefore = candlesAll
				.Where (c => c.OpenTimeUtc < entryUtc)
				.ToList ();

			// sanity: должны быть и до, и после
			Assert.Contains (candlesAll, c => c.OpenTimeUtc >= entryUtc);
			Assert.Equal (6, candlesBefore.Count); // 6 баров по 1h до entry

			var featsAll = TargetLevelFeatureBuilder.Build (
				entryUtc: entryUtc,
				goLong: goLong,
				strongSignal: strongSignal,
				dayMinMove: dayMinMove,
				entryPrice: entryPrice,
				candles1h: candlesAll);

			var featsBefore = TargetLevelFeatureBuilder.Build (
				entryUtc: entryUtc,
				goLong: goLong,
				strongSignal: strongSignal,
				dayMinMove: dayMinMove,
				entryPrice: entryPrice,
				candles1h: candlesBefore);

			// Если TargetLevelFeatureBuilder вдруг начнёт смотреть на бары >= entryUtc,
			// featsAll и featsBefore разъедутся.
			Assert.Equal (featsAll.Length, featsBefore.Length);
			for (int i = 0; i < featsAll.Length; i++)
				{
				Assert.Equal (featsAll[i], featsBefore[i], 6);
				}
			}

		// ========== 4. MinuteDelayedEntryEvaluator: минутки после baseline-выхода не влияют на исход ==========

		[Fact]
		public void MinuteDelayedEntryEvaluator_Ignores_Minutes_After_Baseline_Exit ()
			{
			var nyTz = TimeZones.NewYork;

			// dayStartUtc — "утро" дня, от которого считаем delayed-окно.
			var dayStartUtc = new DateTime (2025, 5, 20, 1, 0, 0, DateTimeKind.Utc);
			var baselineExit = Windowing.ComputeBaselineExitUtc (dayStartUtc, nyTz);

			double entryPrice = 100.0;
			double dayMinMove = 0.03;
			bool goLong = true;
			bool goShort = false;
			bool strongSignal = true;
			double delayFactor = 0.35;
			double maxDelayHours = 4.0;

			// Минутки от dayStart до baselineExit + 3 часа "будущего".
			var day1mBase = BuildMinuteCandles (dayStartUtc, baselineExit.AddHours (3), entryPrice, 0.0003);

			// Копия, в которой после baselineExit мы ставим чудовищные шпильки.
			var day1mMutated = day1mBase
				.Select (c => new Candle1m
					{
					OpenTimeUtc = c.OpenTimeUtc,
					Open = c.Open,
					High = c.High,
					Low = c.Low,
					Close = c.Close,
					})
				.ToList ();

			foreach (var m in day1mMutated.Where (m => m.OpenTimeUtc >= baselineExit))
				{
				m.High *= 2.0;
				m.Low *= 0.5;
				m.Close *= 1.5;
				}

			var res1 = MinuteDelayedEntryEvaluator.Evaluate (
				day1m: day1mBase,
				dayStartUtc: dayStartUtc,
				goLong: goLong,
				goShort: goShort,
				entryPrice12: entryPrice,
				dayMinMove: dayMinMove,
				strongSignal: strongSignal,
				delayFactor: delayFactor,
				maxDelayHours: maxDelayHours,
				nyTz: nyTz);

			var res2 = MinuteDelayedEntryEvaluator.Evaluate (
				day1m: day1mMutated,
				dayStartUtc: dayStartUtc,
				goLong: goLong,
				goShort: goShort,
				entryPrice12: entryPrice,
				dayMinMove: dayMinMove,
				strongSignal: strongSignal,
				delayFactor: delayFactor,
				maxDelayHours: maxDelayHours,
				nyTz: nyTz);

			// Если внутри Evaluate фильтрация по [dayStart; exit) сломается,
			// изменения после baselineExit начнут менять результат.
			Assert.Equal (res1.Executed, res2.Executed);
			Assert.Equal (res1.Result, res2.Result);
			Assert.Equal (res1.TpPct, res2.TpPct, 6);
			Assert.Equal (res1.SlPct, res2.SlPct, 6);
			Assert.Equal (res1.TargetEntryPrice, res2.TargetEntryPrice, 6);
			Assert.Equal (res1.ExecutedAtUtc, res2.ExecutedAtUtc);
			}

		// ========== Вспомогательные генераторы свечей ==========

		/// <summary>
		/// Простейший генератор 1m-свечей:
		/// - OpenTimeUtc идёт с шагом 1 минута;
		/// - цена плавно дрейфует с заданным шагом;
		/// - High/Low чуть выше/ниже.
		/// Это достаточно, чтобы PathLabeler / delayed-логика отработали.
		/// </summary>
		private static List<Candle1m> BuildMinuteCandles (
			DateTime startUtc,
			DateTime endUtc,
			double startPrice,
			double stepPerMinute )
			{
			var result = new List<Candle1m> ();
			var t = startUtc;
			double price = startPrice;

			while (t < endUtc)
				{
				double high = price * (1.0 + 0.001);
				double low = price * (1.0 - 0.001);

				result.Add (new Candle1m
					{
					OpenTimeUtc = t,
					Open = price,
					High = high,
					Low = low,
					Close = price,
					});

				price *= 1.0 + stepPerMinute;
				t = t.AddMinutes (1);
				}

			return result;
			}

		/// <summary>
		/// Простейший генератор 1h-свечей:
		/// - OpenTimeUtc идёт с шагом 1 час;
		/// - цена плавно дрейфует с заданным шагом;
		/// - High/Low чуть шире вокруг Close.
		/// Достаточно для TargetLevelFeatureBuilder.
		/// </summary>
		private static List<Candle1h> BuildHourlyCandles (
			DateTime startUtc,
			int hours,
			double basePrice,
			double stepPerHour )
			{
			var result = new List<Candle1h> ();
			var t = startUtc;
			double price = basePrice;

			for (int i = 0; i < hours; i++)
				{
				double open = price;
				double close = price * (1.0 + stepPerHour);
				double high = Math.Max (open, close) * 1.001;
				double low = Math.Min (open, close) * 0.999;

				result.Add (new Candle1h
					{
					OpenTimeUtc = t,
					Open = open,
					High = high,
					Low = low,
					Close = close,
					});

				price = close;
				t = t.AddHours (1);
				}

			return result;
			}
		}
	}
