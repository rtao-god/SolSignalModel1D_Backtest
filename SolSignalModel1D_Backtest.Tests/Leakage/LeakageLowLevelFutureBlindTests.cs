using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Tests.Leakage
	{
	/// <summary>
	/// Низкоуровневые тесты на "future-blindness" DataBuilder/Labeler.
	/// Идея: мутируем хвост свечей после момента T и проверяем,
	/// что строки, построенные ДО "защищённой" границы, не изменились.
	///
	/// Важно:
	/// - таргет (TrueLabel) по определению использует forward-окно baseline;
	/// - поэтому "защищённая граница" должна быть ДО mutateAfterUtc с запасом по горизонту.
	/// </summary>
	public sealed class LeakageLowLevelFutureBlindTests
		{
		[Fact]
		public void DataBuilder_Features_DoNotDepend_OnFutureCandles ()
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

			var buildOriginal = RowBuilder.BuildDailyRows (
				solWinTrain: solWinTrain,
				btcWinTrain: btcWinTrain,
				paxgWinTrain: paxgWinTrain,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngHistory,
				dxySeries: dxySeries,
				extraDaily: null,
				nyTz: nyTz);

			var rowsOriginal = buildOriginal.LabeledRows
				.OrderBy (r => r.EntryUtc.Value)
				.ToList ();

			Assert.NotEmpty (rowsOriginal);

			// EntryUtc берём явно из causal-части, без record-extension (устраняем ambiguous).
			var lastRowEntryUtc = rowsOriginal.Last ().Causal.EntryUtc.Value;

			var mutateAfterUtc = lastRowEntryUtc.AddDays (-1);
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

			var buildMutated = RowBuilder.BuildDailyRows (
				solWinTrain: solWinTrainMut,
				btcWinTrain: btcWinTrainMut,
				paxgWinTrain: paxgWinTrainMut,
				solAll6h: solAll6hMut,
				solAll1m: solAll1mMut,
				fngHistory: fngHistory,
				dxySeries: dxySeries,
				extraDaily: null,
				nyTz: nyTz);

			var rowsMutated = buildMutated.LabeledRows
				.OrderBy (r => r.EntryUtc.Value)
				.ToList ();

			Assert.NotEmpty (rowsMutated);

			var safeOriginal = rowsOriginal
				.Where (r => r.Causal.EntryUtc.Value <= protectedBoundaryUtc)
				.OrderBy (r => r.EntryUtc.Value)
				.ToList ();

			var safeMutated = rowsMutated
				.Where (r => r.Causal.EntryUtc.Value <= protectedBoundaryUtc)
				.OrderBy (r => r.EntryUtc.Value)
				.ToList ();

			Assert.NotEmpty (safeOriginal);
			Assert.Equal (safeOriginal.Count, safeMutated.Count);

			for (int i = 0; i < safeOriginal.Count; i++)
				{
				var a = safeOriginal[i];
				var b = safeMutated[i];

				Assert.Equal (a.EntryUtc.Value, b.EntryUtc.Value);
				Assert.Equal (a.Causal.EntryUtc.Value, b.Causal.EntryUtc.Value);

				var fa = a.Causal.FeaturesVector.Span;
				var fb = b.Causal.FeaturesVector.Span;

				Assert.Equal (fa.Length, fb.Length);

				for (int j = 0; j < fa.Length; j++)
					{
					AssertAlmostEqual (fa[j], fb[j], 1e-9,
						$"FeatureVector[{j}] differs for EntryUtc={a.Causal.EntryUtc.Value:O}");
					}

				AssertAlmostEqual (a.Causal.SolRet30, b.Causal.SolRet30, 1e-9, "SolRet30 mismatch");
				AssertAlmostEqual (a.Causal.BtcRet30, b.Causal.BtcRet30, 1e-9, "BtcRet30 mismatch");
				AssertAlmostEqual (a.Causal.AtrPct, b.Causal.AtrPct, 1e-9, "AtrPct mismatch");
				AssertAlmostEqual (a.Causal.DynVol, b.Causal.DynVol, 1e-9, "DynVol mismatch");
				}
			}

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

			var rowsOriginal = RowBuilder.BuildDailyRows (
					solWinTrain,
					btcWinTrain,
					paxgWinTrain,
					solAll6h,
					solAll1m,
					fngHistory,
					dxySeries,
				extraDaily: null,
				nyTz: nyTz)
				.LabeledRows
				.OrderBy (r => r.EntryUtc.Value)
				.ToList ();

			Assert.NotEmpty (rowsOriginal);

			var lastRowEntryUtc = rowsOriginal.Last ().Causal.EntryUtc.Value;
			var mutateAfterUtc = lastRowEntryUtc.AddDays (-1);
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

			var rowsMutated = RowBuilder.BuildDailyRows (
					solWinTrain: solWinTrainMut,
					btcWinTrain: btcWinTrainMut,
					paxgWinTrain: paxgWinTrainMut,
					solAll6h: solAll6hMut,
					solAll1m: solAll1mMut,
					fngHistory: fngHistory,
					dxySeries: dxySeries,
					extraDaily: null,
					nyTz: nyTz)
				.LabeledRows
				.OrderBy (r => r.EntryUtc.Value)
				.ToList ();

			Assert.NotEmpty (rowsMutated);

			var safeOriginal = rowsOriginal
				.Where (r => r.Causal.EntryUtc.Value <= protectedBoundaryUtc)
				.OrderBy (r => r.EntryUtc.Value)
				.ToList ();

			var safeMutated = rowsMutated
				.Where (r => r.Causal.EntryUtc.Value <= protectedBoundaryUtc)
				.OrderBy (r => r.EntryUtc.Value)
				.ToList ();

			Assert.NotEmpty (safeOriginal);
			Assert.Equal (safeOriginal.Count, safeMutated.Count);

			for (int i = 0; i < safeOriginal.Count; i++)
				{
				var a = safeOriginal[i];
				var b = safeMutated[i];

				Assert.Equal (a.EntryUtc.Value, b.EntryUtc.Value);

				Assert.Equal (a.TrueLabel, b.TrueLabel);
				Assert.Equal (a.FactMicroUp, b.FactMicroUp);
				Assert.Equal (a.FactMicroDown, b.FactMicroDown);

				AssertAlmostEqual (a.Causal.MinMove, b.Causal.MinMove, 1e-9, "MinMove mismatch");
				}
			}

		private static (
			List<Candle6h> solWinTrain,
			List<Candle6h> btcWinTrain,
			List<Candle6h> paxgWinTrain,
			List<Candle6h> solAll6h,
			List<Candle6h> solAll1hDummy,
			List<Candle1m> solAll1m,
			Dictionary<DateTime, double> fngHistory,
			Dictionary<DateTime, double> dxySeries
		) BuildSyntheticSeriesWith1h ( TimeZoneInfo nyTz )
			{
			const int total6h = 260;

			var solWinTrain = new List<Candle6h> (total6h);
			var btcWinTrain = new List<Candle6h> (total6h);
			var paxgWinTrain = new List<Candle6h> (total6h);
			var solAll6h = new List<Candle6h> (total6h);

			var startLocal = new DateTime (2021, 1, 4, 7, 0, 0, DateTimeKind.Unspecified);

			for (int i = 0; i < total6h; i++)
				{
				var openLocal = startLocal.AddHours (6 * i);
				var openUtc = TimeZoneInfo.ConvertTimeToUtc (openLocal, nyTz);

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

			var firstMinuteLocal = startLocal;

			var lastOpenLocal = startLocal.AddHours (6 * (total6h - 1));
			var lastOpenUtc = TimeZoneInfo.ConvertTimeToUtc (lastOpenLocal, nyTz);

			var lastExitUtc = NyWindowing.ComputeBaselineExitUtc (new EntryUtc (lastOpenUtc), nyTz).Value;
			var lastExitLocal = TimeZoneInfo.ConvertTimeFromUtc (lastExitUtc, nyTz);

			var totalMinutes = (int) Math.Ceiling ((lastExitLocal - firstMinuteLocal).TotalMinutes) + 60;
			if (totalMinutes <= 0)
				totalMinutes = 1;

			var solAll1m = new List<Candle1m> (totalMinutes);

			for (int i = 0; i < totalMinutes; i++)
				{
				var minuteLocal = firstMinuteLocal.AddMinutes (i);
				var minuteUtc = TimeZoneInfo.ConvertTimeToUtc (minuteLocal, nyTz);

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

			var firstMinuteUtc = TimeZoneInfo.ConvertTimeToUtc (firstMinuteLocal, nyTz);
			var day = firstMinuteUtc.ToCausalDateUtc ();
			var lastDay = lastExitUtc.ToCausalDateUtc ();

			var fngHistory = new Dictionary<DateTime, double> ();
			var dxySeries = new Dictionary<DateTime, double> ();

			while (day <= lastDay)
				{
				fngHistory[day] = 50.0;
                // Контракт DXY: значение "now" должно быть > 0.
                dxySeries[day] = 100.0;
                day = day.AddDays (1);
				}

			var solAll1hDummy = new List<Candle6h> ();

			return (solWinTrain, btcWinTrain, paxgWinTrain, solAll6h, solAll1hDummy, solAll1m, fngHistory, dxySeries);
			}

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

		private static void AssertAlmostEqual ( double expected, double actual, double tol, string message )
			{
			if (double.IsNaN (expected) && double.IsNaN (actual))
				return;

			var diff = Math.Abs (expected - actual);
			Assert.True (diff <= tol, $"{message}: expected={expected}, actual={actual}, diff={diff}, tol={tol}");
			}
		}
	}

