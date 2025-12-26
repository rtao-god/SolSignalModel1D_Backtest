using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Utils.Time;
using CoreNyWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.NyWindowing;

namespace SolSignalModel1D_Backtest.Tests.Data.DataBuilder
	{
	/// <summary>
	/// Структурный тест:
	/// фичи дня D не должны зависеть от того, какой close у свечи,
	/// покрывающей baseline-exit.
	///
	/// Это проверяет отсутствие forward-lookups через future-close на границе окна.
	/// </summary>
	public sealed class RowBuilderFeatureLeakageTests
		{
		[Fact]
		public void Features_DoNotChange_WhenBaselineExitCloseChanges ()
			{
			var tz = CoreNyWindowing.NyTz;

			const int total6h = 300;
			var start = new DateTime (2020, 1, 1, 2, 0, 0, DateTimeKind.Utc);

			var solAll6h_A = new List<Candle6h> (total6h);
			var solAll6h_B = new List<Candle6h> (total6h);
			var btcAll6h = new List<Candle6h> (total6h);
			var paxgAll6h = new List<Candle6h> (total6h);

			for (int i = 0; i < total6h; i++)
				{
				var t = start.AddHours (6 * i);
				double solPrice = 100.0 + i;
				double btcPrice = 50.0 + i * 0.5;
				double goldPrice = 1500.0 + i * 0.2;

				solAll6h_A.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = solPrice,
					Close = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0
					});
				solAll6h_B.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = solPrice,
					Close = solPrice,
					High = solPrice + 1.0,
					Low = solPrice - 1.0
					});

				btcAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = btcPrice,
					Close = btcPrice,
					High = btcPrice + 1.0,
					Low = btcPrice - 1.0
					});

				paxgAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = goldPrice,
					Close = goldPrice,
					High = goldPrice + 1.0,
					Low = goldPrice - 1.0
					});
				}

			// Макро-ряды.
			var fng = new Dictionary<DateTime, double> ();
			var dxy = new Dictionary<DateTime, double> ();

			var startDay = start.ToCausalDateUtc ();
			var firstDate = startDay.AddDays (-60);
			var lastDate = startDay.AddDays (400);

			for (var d = firstDate; d <= lastDate; d = d.AddDays (1))
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fng[key] = 50;
				dxy[key] = 100.0;
				}

			Dictionary<DateTime, (double Funding, double OI)>? extraDaily = null;

			// 1m-серия (одна и та же для A/B).
			var solAll1m = new List<Candle1m> (total6h * 6 * 60);
			var minutesStart = start;
			int totalMinutes = total6h * 6 * 60;

			for (int i = 0; i < totalMinutes; i++)
				{
				var t = minutesStart.AddMinutes (i);
				double price = 100.0 + i * 0.0001;

				solAll1m.Add (new Candle1m
					{
					OpenTimeUtc = t,
					Open = price,
					Close = price,
					High = price + 0.0005,
					Low = price - 0.0005
					});
				}

			// Строим A, чтобы выбрать корректный entryUtc (RowBuilder может пропускать warm-up/выходные).
			var buildA0 = RowBuilder.BuildDailyRows (
				solWinTrain: solAll6h_A,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h_A,
				solAll1m: solAll1m,
				fngHistory: fng,
				dxySeries: dxy,
				extraDaily: extraDaily,
				nyTz: tz);

			var rowsA0 = buildA0.LabeledRows
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			Assert.True (rowsA0.Count > 50, "rowsA слишком мало для теста.");

			var entryUtc = rowsA0[rowsA0.Count / 3].Causal.DateUtc;

			// Находим baseline-exit и 6h-свечу, которая его покрывает в B-сценарии.
			var exitUtc = CoreNyWindowing.ComputeBaselineExitUtc (entryUtc, tz);

			int exitIdx = -1;
			for (int i = 0; i < solAll6h_B.Count; i++)
				{
				var startUtc = solAll6h_B[i].OpenTimeUtc;
				var endUtc = (i + 1 < solAll6h_B.Count) ? solAll6h_B[i + 1].OpenTimeUtc : startUtc.AddHours (6);

				if (exitUtc >= startUtc && exitUtc < endUtc)
					{
					exitIdx = i;
					break;
					}
				}

			Assert.True (exitIdx >= 0, "Не удалось найти 6h-свечу, покрывающую baseline-exit.");

			// Мутируем future-close на границе окна.
			solAll6h_B[exitIdx].Close *= 10.0;
			solAll6h_B[exitIdx].High = solAll6h_B[exitIdx].Close + 1.0;
			solAll6h_B[exitIdx].Low = solAll6h_B[exitIdx].Close - 1.0;

			// Считаем A/B.
			var buildA = RowBuilder.BuildDailyRows (
				solWinTrain: solAll6h_A,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h_A,
				solAll1m: solAll1m,
				fngHistory: fng,
				dxySeries: dxy,
				extraDaily: extraDaily,
				nyTz: tz);

			var buildB = RowBuilder.BuildDailyRows (
				solWinTrain: solAll6h_B,
				btcWinTrain: btcAll6h,
				paxgWinTrain: paxgAll6h,
				solAll6h: solAll6h_B,
				solAll1m: solAll1m,
				fngHistory: fng,
				dxySeries: dxy,
				extraDaily: extraDaily,
				nyTz: tz);

			var rowsA = buildA.LabeledRows;
			var rowsB = buildB.LabeledRows;

			var rowA = rowsA.SingleOrDefault (r => r.Causal.DateUtc == entryUtc);
			var rowB = rowsB.SingleOrDefault (r => r.Causal.DateUtc == entryUtc);

			Assert.NotNull (rowA);
			Assert.NotNull (rowB);

			// TrueLabel не обязан зависеть от 6h close на границе окна (лейбл строится по 1m-path).
			Assert.Equal (rowA!.TrueLabel, rowB!.TrueLabel);

			// Фичи не должны зависеть от future-close.
			AssertFeatureVectorsEqual (rowA.Causal, rowB.Causal);
			}

		private static void AssertFeatureVectorsEqual ( CausalDataRow a, CausalDataRow b, int precisionDigits = 10 )
			{
			var va = a.FeaturesVector.Span;
			var vb = b.FeaturesVector.Span;

			Assert.Equal (va.Length, vb.Length);

			for (int i = 0; i < va.Length; i++)
				{
				Assert.Equal (va[i], vb[i], precisionDigits);
				}
			}
		}
	}
