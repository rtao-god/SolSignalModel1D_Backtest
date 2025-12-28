using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.Utils.Time;
using Xunit;
using CoreNyWindowing = SolSignalModel1D_Backtest.Core.Causal.Time.NyWindowing;
using SolSignalModel1D_Backtest.Core.Causal.Utils.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.DataBuilder;

namespace SolSignalModel1D_Backtest.Tests.Data.Indicators
	{
	public sealed class IndicatorsLeakageTests
		{
		[Fact]
		public void Features_DoNotChange_WhenFutureMacroSeriesAreMutated ()
			{
			var tz = CoreNyWindowing.NyTz;

			const int days = 260;

			BuildSyntheticMarket (
				days: days,
				out var solAll6h,
				out var btcAll6h,
				out var paxgAll6h,
				out var solWinTrain,
				out var btcWinTrain,
				out var paxgWinTrain,
				out var solAll1m,
				out var fngBase,
				out var dxyBase);

			var resA = RowBuilder.BuildDailyRows (
				solWinTrain: solWinTrain,
				btcWinTrain: btcWinTrain,
				paxgWinTrain: paxgWinTrain,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngBase,
				dxySeries: dxyBase,
				extraDaily: null,
				nyTz: tz);

			var rowsA = resA.LabeledRows
				.OrderBy (r => r.Causal.EntryUtc.Value)
				.ToList ();

			Assert.True (rowsA.Count > 50, "rowsA слишком мало для теста");

			var cutoffUtc = rowsA[rowsA.Count / 3].Causal.EntryUtc.Value;
			var mutateFromDay = cutoffUtc.ToCausalDateUtc ().AddDays (10);

			var fngB = new Dictionary<DateTime, double> (fngBase);
			var dxyB = new Dictionary<DateTime, double> (dxyBase);

			foreach (var key in fngB.Keys.ToList ())
				{
				if (key.ToCausalDateUtc () > mutateFromDay)
					fngB[key] = fngB[key] + 40.0;
				}

			foreach (var key in dxyB.Keys.ToList ())
				{
				if (key.ToCausalDateUtc () > mutateFromDay)
					dxyB[key] = dxyB[key] * 10.0;
				}

			var resB = RowBuilder.BuildDailyRows (
				solWinTrain: solWinTrain,
				btcWinTrain: btcWinTrain,
				paxgWinTrain: paxgWinTrain,
				solAll6h: solAll6h,
				solAll1m: solAll1m,
				fngHistory: fngB,
				dxySeries: dxyB,
				extraDaily: null,
				nyTz: tz);

			var rowsB = resB.LabeledRows
				.OrderBy (r => r.Causal.EntryUtc.Value)
				.ToList ();

			var dictA = rowsA.ToDictionary (r => r.Causal.EntryUtc.Value, r => r);
			var dictB = rowsB.ToDictionary (r => r.Causal.EntryUtc.Value, r => r);

			foreach (var kv in dictA)
				{
				var dateUtc = kv.Key;
				if (dateUtc > cutoffUtc)
					continue;

				Assert.True (dictB.ContainsKey (dateUtc), $"Во втором наборе нет строки для {dateUtc:O}");

				var a = kv.Value;
				var b = dictB[dateUtc];

				Assert.Equal (a.TrueLabel, b.TrueLabel);
				Assert.Equal (a.FactMicroUp, b.FactMicroUp);
				Assert.Equal (a.FactMicroDown, b.FactMicroDown);

				var fa = a.Causal.FeaturesVector.Span;
				var fb = b.Causal.FeaturesVector.Span;

				Assert.Equal (fa.Length, fb.Length);
				for (int i = 0; i < fa.Length; i++)
					{
					Assert.Equal (fa[i], fb[i], 12);
					}
				}
			}

		// ===== helpers =====

		private static void BuildSyntheticMarket (
			int days,
			out List<Candle6h> solAll6h,
			out List<Candle6h> btcAll6h,
			out List<Candle6h> paxgAll6h,
			out List<Candle6h> solWinTrain,
			out List<Candle6h> btcWinTrain,
			out List<Candle6h> paxgWinTrain,
			out List<Candle1m> solAll1m,
			out Dictionary<DateTime, double> fng,
			out Dictionary<DateTime, double> dxy )
			{
			int total6h = days * 4;
			int totalMinutes = days * 24 * 60;

			var start = new DateTime (2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			solAll6h = new List<Candle6h> (total6h);
			btcAll6h = new List<Candle6h> (total6h);
			paxgAll6h = new List<Candle6h> (total6h);
			solAll1m = new List<Candle1m> (totalMinutes);

			for (int i = 0; i < total6h; i++)
				{
				var t = start.AddHours (6 * i);

				double sol = 100.0 + i * 0.1;
				double btc = 500.0 + i * 0.3;
				double paxg = 1500.0 + i * 0.05;

				solAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = sol,
					High = sol * 1.01,
					Low = sol * 0.99,
					Close = sol * 1.005
					});

				btcAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = btc,
					High = btc * 1.01,
					Low = btc * 0.99,
					Close = btc * 1.005
					});

				paxgAll6h.Add (new Candle6h
					{
					OpenTimeUtc = t,
					Open = paxg,
					High = paxg * 1.01,
					Low = paxg * 0.99,
					Close = paxg * 1.005
					});
				}

			for (int i = 0; i < totalMinutes; i++)
				{
				var t = start.AddMinutes (i);

				double p = 100.0 + i * 0.0002;

				solAll1m.Add (new Candle1m
					{
					OpenTimeUtc = t,
					Open = p,
					High = p * 1.0005,
					Low = p * 0.9995,
					Close = p
					});
				}

			solWinTrain = BuildDailyWinTrainFromAll6h (solAll6h);
			btcWinTrain = BuildDailyWinTrainFromAll6h (btcAll6h);
			paxgWinTrain = BuildDailyWinTrainFromAll6h (paxgAll6h);

			fng = new Dictionary<DateTime, double> ();
			dxy = new Dictionary<DateTime, double> ();

			var first = start.ToCausalDateUtc ().AddDays (-260);
			var last = start.ToCausalDateUtc ().AddDays (days + 260);

			for (var d = first; d <= last; d = d.AddDays (1))
				{
				var key = new DateTime (d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
				fng[key] = 50.0;
				dxy[key] = 100.0 + (d.Day % 7) * 0.1;
				}
			}

		private static List<Candle6h> BuildDailyWinTrainFromAll6h ( List<Candle6h> all6h )
			{
			return all6h
				.Select (c => new Candle6h
					{
					OpenTimeUtc = c.OpenTimeUtc,
					Open = c.Open,
					High = c.High,
					Low = c.Low,
					Close = c.Close
					})
				.ToList ();
			}
		}
	}

