using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage
	{
	/// <summary>
	/// Низкоуровневые тесты на "future-blindness" DataBuilder/Labeler.
	/// Идея: мутируем хвост свечей после момента T и проверяем,
	/// что строки BacktestRecord "до T" (с учётом baseline-окна) не изменились.
	/// </summary>
	public sealed class LeakageLowLevelFutureBlindTests
		{
		/// <summary>
		/// DataBuilder_Features_DoNotDepend_OnFutureCandles:
		/// фичи в BacktestRecord для безопасных дат не зависят от хвоста 6h/1m после T.
		/// </summary>
		[Fact]
		public void DataBuilder_Features_DoNotDepend_OnFutureCandles ()
			{
			// 1) Строим синтетические ряды 6h/1m + FNG/DXY и BacktestRecord из них.
			var nyTz = TimeZones.NewYork;

			var (
				solWinTrain,
				btcWinTrain,
				paxgWinTrain,
				solAll6h,
				solAll1m,
				fngHistory,
				dxySeries
			) = BuildSyntheticSeries (nyTz);

			var rowsOriginal = RowBuilder.BuildRowsDaily (
				solWinTrain,
				btcWinTrain,
				paxgWinTrain,
				solAll6h,
				solAll1m,
				fngHistory,
				dxySeries,
				extraDaily: null,
				nyTz: nyTz);

			Assert.NotEmpty (rowsOriginal);

			// 2) Определяем глобальную границу хвоста:
			//   - mutateAfterUtc — откуда начинаем мутировать хвост;
			//   - protectedBoundaryUtc — до каких дат baseline-окно
			//     гарантированно не задевает мутированный хвост.
			var lastRowDate = rowsOriginal.Last ().Date;
			var mutateAfterUtc = lastRowDate.AddDays (-1);   // хвост совсем в конце
			var protectedBoundaryUtc = mutateAfterUtc.AddDays (-3); // запас на Fri→Mon

			// 3) Строим независимую копию свечей и жёстко мутируем хвост.
			var solWinTrainMut = CloneCandles6h (solWinTrain);
			var btcWinTrainMut = CloneCandles6h (btcWinTrain);
			var paxgWinTrainMut = CloneCandles6h (paxgWinTrain);
			var solAll6hMut = CloneCandles6h (solAll6h);
			var solAll1mMut = CloneCandles1m (solAll1m);

			MutateTail6h (solWinTrainMut, mutateAfterUtc);
			MutateTail6h (btcWinTrainMut, mutateAfterUtc);
			MutateTail6h (paxgWinTrainMut, mutateAfterUtc);
			MutateTail6h (solAll6hMut, mutateAfterUtc);
			MutateTail1m (solAll1mMut, mutateAfterUtc);

			var rowsMutated = RowBuilder.BuildRowsDaily (
				solWinTrainMut,
				btcWinTrainMut,
				paxgWinTrainMut,
				solAll6hMut,
				solAll1mMut,
				fngHistory,
				dxySeries,
				extraDaily: null,
				nyTz: nyTz);

			Assert.NotEmpty (rowsMutated);

			// 4) Берём только "безопасные" строки:
			// их baseline-окно гарантированно полностью до mutateAfterUtc,
			// значит любые изменения после mutateAfterUtc не должны их трогать.
			var safeOriginal = rowsOriginal
				.Where (r => r.Causal.DateUtc <= protectedBoundaryUtc)
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			var safeMutated = rowsMutated
				.Where (r => r.Causal.DateUtc <= protectedBoundaryUtc)
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			Assert.NotEmpty (safeOriginal);
			Assert.Equal (safeOriginal.Count, safeMutated.Count);

			for (int i = 0; i < safeOriginal.Count; i++)
				{
				var a = safeOriginal[i];
				var b = safeMutated[i];

				// Структура набора строк не должна меняться.
				Assert.Equal (a.Causal.DateUtc, b.Causal.DateUtc);

				// Размерность фич такая же.
				Assert.Equal (a.Causal.Features.Length, b.Causal.Features.Length);

				// Все фичи должны совпадать (future-blind).
				for (int j = 0; j < a.Causal.Features.Length; j++)
					{
					AssertAlmostEqual (a.Causal.Features[j], b.Causal.Features[j], 1e-9,
						$"Feature[{j}] differs for Date={a.Causal.DateUtc:O}");
					}

				// Дополнительно проверяем несколько "сырьевых" полей,
				// которые не входят во вектор фич, но тоже должны быть future-blind.
				AssertAlmostEqual (a.Causal.SolRet30, b.Causal.SolRet30, 1e-9, "SolRet30 mismatch");
				AssertAlmostEqual (a.Causal.BtcRet30, b.Causal.BtcRet30, 1e-9, "BtcRet30 mismatch");
				AssertAlmostEqual (a.Causal.AtrPct, b.Causal.AtrPct, 1e-9, "AtrPct mismatch");
				AssertAlmostEqual (a.Causal.DynVol, b.Causal.DynVol, 1e-9, "DynVol mismatch");
				}
			}

		/// <summary>
		/// Labeler_Targets_DoNotDepend_OnFutureCandles:
		/// таргеты (Label / SolFwd1 / path-based / micro) для безопасных дат
		/// не зависят от хвоста 6h/1m после T.
		/// </summary>
		[Fact]
		public void Labeler_Targets_DoNotDepend_OnFutureCandles ()
			{
			var nyTz = TimeZones.NewYork;

			var (
				solWinTrain,
				btcWinTrain,
				paxgWinTrain,
				solAll6h,
				solAll1m,
				fngHistory,
				dxySeries
			) = BuildSyntheticSeries (nyTz);

			var rowsOriginal = RowBuilder.BuildRowsDaily (
				solWinTrain,
				btcWinTrain,
				paxgWinTrain,
				solAll6h,
				solAll1m,
				fngHistory,
				dxySeries,
				extraDaily: null,
				nyTz: nyTz);

			Assert.NotEmpty (rowsOriginal);

			var lastRowDate = rowsOriginal.Last ().Date;
			var mutateAfterUtc = lastRowDate.AddDays (-1);
			var protectedBoundaryUtc = mutateAfterUtc.AddDays (-3);

			var solWinTrainMut = CloneCandles6h (solWinTrain);
			var btcWinTrainMut = CloneCandles6h (btcWinTrain);
			var paxgWinTrainMut = CloneCandles6h (paxgWinTrain);
			var solAll6hMut = CloneCandles6h (solAll6h);
			var solAll1mMut = CloneCandles1m (solAll1m);

			MutateTail6h (solWinTrainMut, mutateAfterUtc);
			MutateTail6h (btcWinTrainMut, mutateAfterUtc);
			MutateTail6h (paxgWinTrainMut, mutateAfterUtc);
			MutateTail6h (solAll6hMut, mutateAfterUtc);
			MutateTail1m (solAll1mMut, mutateAfterUtc);

			var rowsMutated = RowBuilder.BuildRowsDaily (
				solWinTrainMut,
				btcWinTrainMut,
				paxgWinTrainMut,
				solAll6hMut,
				solAll1mMut,
				fngHistory,
				dxySeries,
				extraDaily: null,
				nyTz: nyTz);

			Assert.NotEmpty (rowsMutated);

			var safeOriginal = rowsOriginal
				.Where (r => r.Causal.DateUtc <= protectedBoundaryUtc)
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			var safeMutated = rowsMutated
				.Where (r => r.Causal.DateUtc <= protectedBoundaryUtc)
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			Assert.NotEmpty (safeOriginal);
			Assert.Equal (safeOriginal.Count, safeMutated.Count);

			for (int i = 0; i < safeOriginal.Count; i++)
				{
				var a = safeOriginal[i];
				var b = safeMutated[i];

				Assert.Equal (a.Causal.DateUtc, b.Causal.DateUtc);

				// Основной path-based таргет.
				Assert.Equal (a.Forward.TrueLabel, b.Forward.TrueLabel);

				// Micro-facts (по pathUp/pathDown внутри baseline-окна).
				Assert.Equal (a.FactMicroUp, b.FactMicroUp);
				Assert.Equal (a.FactMicroDown, b.FactMicroDown);

				// Таргет по close на baseline-горизонте.
				AssertAlmostEqual (a.SolFwd1, b.SolFwd1, 1e-9, "SolFwd1 mismatch");

				// Path-first метрики.
				Assert.Equal (a.Forward.PathFirstPassDir, b.Forward.PathFirstPassDir);
				Assert.Equal (a.Forward.PathFirstPassTimeUtc, b.Forward.PathFirstPassTimeUtc);
				AssertAlmostEqual (a.Forward.PathReachedUpPct, b.Forward.PathReachedUpPct, 1e-9, "PathReachedUpPct mismatch");
				AssertAlmostEqual (a.Forward.PathReachedDownPct, b.Forward.PathReachedDownPct, 1e-9, "PathReachedDownPct mismatch");

				// MinMove тоже должен быть future-blind (строится каузально по истории rows).
				AssertAlmostEqual (a.MinMove, b.MinMove, 1e-9, "MinMove mismatch");
				}
			}

		// ====================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ======================

		/// <summary>
		/// Строит синтетические 6h/1m свечи и ряды FNG/DXY так,
		/// чтобы RowBuilder мог спокойно посчитать все индикаторы (RSI/ATR/200SMA).
		/// Сгенерированные ряды достаточно длинные (260 6h-баров ~ 65 дней),
		/// чтобы прошли все проверки "недостаточно истории".
		/// </summary>
		private static (
			List<Candle6h> solWinTrain,
			List<Candle6h> btcWinTrain,
			List<Candle6h> paxgWinTrain,
			List<Candle6h> solAll6h,
			List<Candle6h> solAll1hDummy, // не используется, оставлен для совместимости сигнатуры
			List<Candle1m> solAll1m,
			Dictionary<DateTime, double> fngHistory,
			Dictionary<DateTime, double> dxySeries
		) BuildSyntheticSeriesWith1h ( TimeZoneInfo nyTz )
			{
			const int total6h = 260; // > 200 для SMA, с запасом для 30-дневных ретурнов

			var solWinTrain = new List<Candle6h> (total6h);
			var btcWinTrain = new List<Candle6h> (total6h);
			var paxgWinTrain = new List<Candle6h> (total6h);
			var solAll6h = new List<Candle6h> (total6h);

			// Стартуем с понедельника 08:00 NY локального времени,
			// чтобы baseline-окна были "нормальными" и без специальных краёв.
			var startLocal = new DateTime (2021, 1, 4, 8, 0, 0, DateTimeKind.Unspecified);

			for (int i = 0; i < total6h; i++)
				{
				var openLocal = startLocal.AddHours (6 * i);
				var openUtc = TimeZoneInfo.ConvertTimeToUtc (openLocal, nyTz);

				// Простая линейная динамика, главное — положительные цены.
				double solPrice = 100.0 + 0.1 * i;
				double btcPrice = 200.0 + 0.2 * i;
				double goldPrice = 50.0 + 0.05 * i;

				solWinTrain.Add (new Candle6h
					{
					OpenTimeUtc = openUtc,
					Open = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0,
					Close = solPrice + 0.5
					});

				btcWinTrain.Add (new Candle6h
					{
					OpenTimeUtc = openUtc,
					Open = btcPrice,
					High = btcPrice + 1.0,
					Low = btcPrice - 1.0,
					Close = btcPrice + 0.5
					});

				paxgWinTrain.Add (new Candle6h
					{
					OpenTimeUtc = openUtc,
					Open = goldPrice,
					High = goldPrice + 0.5,
					Low = goldPrice - 0.5,
					Close = goldPrice + 0.25
					});

				solAll6h.Add (new Candle6h
					{
					OpenTimeUtc = openUtc,
					Open = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0,
					Close = solPrice + 0.5
					});
				}

			// 1m-свечи: покрываем весь диапазон от первого входа до
			// baseline-выхода для последней 6h-свечи (с запасом).
			var firstMinuteLocal = startLocal;
			var lastOpenLocal = startLocal.AddHours (6 * (total6h - 1));
			var lastOpenUtc = TimeZoneInfo.ConvertTimeToUtc (lastOpenLocal, nyTz);

			var lastExitUtc = Windowing.ComputeBaselineExitUtc (lastOpenUtc, nyTz);
			var lastExitLocal = TimeZoneInfo.ConvertTimeFromUtc (lastExitUtc, nyTz);

			var totalMinutes = (int) Math.Ceiling ((lastExitLocal - firstMinuteLocal).TotalMinutes) + 60;
			if (totalMinutes <= 0)
				totalMinutes = 1;

			var solAll1m = new List<Candle1m> (totalMinutes);

			for (int i = 0; i < totalMinutes; i++)
				{
				var minuteLocal = firstMinuteLocal.AddMinutes (i);
				var minuteUtc = TimeZoneInfo.ConvertTimeToUtc (minuteLocal, nyTz);

				// Небольшой плавный тренд, чтобы PathLabeler мог что-то разметить.
				double price = 100.0 + 0.0005 * i;

				solAll1m.Add (new Candle1m
					{
					OpenTimeUtc = minuteUtc,
					Open = price,
					High = price + 0.0002,
					Low = price - 0.0002,
					Close = price
					});
				}

			// FNG/DXY на каждый календарный день диапазона.
			var fngHistory = new Dictionary<DateTime, double> ();
			var dxySeries = new Dictionary<DateTime, double> ();

			var day = firstMinuteLocal.Causal.DateUtc;
			var lastDay = lastExitLocal.Causal.DateUtc;

			while (day <= lastDay)
				{
				var key = new DateTime (day.Year, day.Month, day.Day);
				fngHistory[key] = 50; // нейтральное значение, главное — непрерывность
				dxySeries[key] = 0.0; // плоский ряд, чтобы 30-дневный change был ~0
				day = day.AddDays (1);
				}

			// 1h нам не нужен для RowBuilder, но возвращаем dummy-список, если вдруг
			// пригодится в дальнейшем расширении теста.
			var solAll1hDummy = new List<Candle6h> ();

			return (solWinTrain, btcWinTrain, paxgWinTrain, solAll6h, solAll1hDummy, solAll1m, fngHistory, dxySeries);
			}

		/// <summary>
		/// Обёртка над BuildSyntheticSeriesWith1h без dummy 1h-списка,
		/// чтобы меньше шуметь в сигнатурах тестов.
		/// </summary>
		private static (
			List<Candle6h> solWinTrain,
			List<Candle6h> btcWinTrain,
			List<Candle6h> paxgWinTrain,
			List<Candle6h> solAll6h,
			List<Candle1m> solAll1m,
			Dictionary<DateTime, double> fngHistory,
			Dictionary<DateTime, double> dxySeries
		) BuildSyntheticSeries ( TimeZoneInfo nyTz )
			{
			var (
				solWinTrain,
				btcWinTrain,
				paxgWinTrain,
				solAll6h,
				_,
				solAll1m,
				fngHistory,
				dxySeries
			) = BuildSyntheticSeriesWith1h (nyTz);

			return (solWinTrain, btcWinTrain, paxgWinTrain, solAll6h, solAll1m, fngHistory, dxySeries);
			}

		/// <summary>
		/// Клонирует список 6h-свечей. Нужен, чтобы "оригинал" и "мутант"
		/// были совершенно независимы в памяти.
		/// </summary>
		private static List<Candle6h> CloneCandles6h ( List<Candle6h> source )
			{
			var res = new List<Candle6h> (source.Count);
			foreach (var c in source)
				{
				res.Add (new Candle6h
					{
					OpenTimeUtc = c.OpenTimeUtc,
					Open = c.Open,
					High = c.High,
					Low = c.Low,
					Close = c.Close
					});
				}
			return res;
			}

		/// <summary>
		/// Клонирует список 1m-свечей.
		/// </summary>
		private static List<Candle1m> CloneCandles1m ( List<Candle1m> source )
			{
			var res = new List<Candle1m> (source.Count);
			foreach (var c in source)
				{
				res.Add (new Candle1m
					{
					OpenTimeUtc = c.OpenTimeUtc,
					Open = c.Open,
					High = c.High,
					Low = c.Low,
					Close = c.Close
					});
				}
			return res;
			}

		/// <summary>
		/// Жёстко мутирует хвост 6h-свечей: всё, что строго ПОСЛЕ mutateAfterUtc,
		/// умножается на 10. Если DataBuilder/Labeler смотрят в будущее,
		/// это гарантированно "подсветит" утечку.
		/// </summary>
		private static void MutateTail6h ( List<Candle6h> candles, DateTime mutateAfterUtc )
			{
			for (int i = 0; i < candles.Count; i++)
				{
				var c = candles[i];
				if (c.OpenTimeUtc > mutateAfterUtc)
					{
					c.Open *= 10.0;
					c.High *= 10.0;
					c.Low *= 10.0;
					c.Close *= 10.0;
					}
				candles[i] = c;
				}
			}

		/// <summary>
		/// Жёстко мутирует хвост 1m-свечей.
		/// </summary>
		private static void MutateTail1m ( List<Candle1m> candles, DateTime mutateAfterUtc )
			{
			for (int i = 0; i < candles.Count; i++)
				{
				var c = candles[i];
				if (c.OpenTimeUtc > mutateAfterUtc)
					{
					c.Open *= 10.0;
					c.High *= 10.0;
					c.Low *= 10.0;
					c.Close *= 10.0;
					}
				candles[i] = c;
				}
			}

		/// <summary>
		/// Сравнение double с допуском, с понятным сообщением об ошибке.
		/// </summary>
		private static void AssertAlmostEqual ( double expected, double actual, double tol, string message )
			{
			if (double.IsNaN (expected) && double.IsNaN (actual))
				return;

			var diff = Math.Abs (expected - actual);
			Assert.True (diff <= tol, $"{message}: expected={expected}, actual={actual}, diff={diff}, tol={tol}");
			}
		}
	}
