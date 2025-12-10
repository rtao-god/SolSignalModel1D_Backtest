using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML.SL;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Sl
	{
	/// <summary>
	/// Тест: SlDatasetBuilder не использует дни с Date > trainUntil
	/// и future-blind к мутациям хвоста по DataRow.
	/// 1m/6h-свечи для простоты одинаковые в A/B (факт пути не трогаем).
	/// </summary>
	public class LeakageSlDatasetTests
		{
		[Fact]
		public void SlDataset_UsesOnlyRows_UntilTrainUntil_AndIsFutureBlind ()
			{
			var allRows = BuildSyntheticRows (30, out var sol6hDict, out var sol1m);

			var maxDate = allRows.Last ().Date;
			var trainUntil = maxDate.AddDays (-10);

			var rowsA = CloneRows (allRows);
			var rowsB = CloneRows (allRows);

			MutateFutureTail (rowsB, trainUntil);

			var dsA = SlDatasetBuilder.Build (
				rows: rowsA,
				sol1h: null,
				sol1m: sol1m,
				sol6hDict: sol6hDict,
				trainUntil: trainUntil,
				tpPct: 0.03,
				slPct: 0.05,
				strongSelector: null
			);

			var dsB = SlDatasetBuilder.Build (
				rows: rowsB,
				sol1h: null,
				sol1m: sol1m,
				sol6hDict: sol6hDict,
				trainUntil: trainUntil,
				tpPct: 0.03,
				slPct: 0.05,
				strongSelector: null
			);

			Assert.All (dsA.MorningRows, r => Assert.True (r.Date <= trainUntil));
			Assert.All (dsB.MorningRows, r => Assert.True (r.Date <= trainUntil));

			Assert.Equal (dsA.Samples.Count, dsB.Samples.Count);

			for (int i = 0; i < dsA.Samples.Count; i++)
				{
				var a = dsA.Samples[i];
				var b = dsB.Samples[i];

				Assert.Equal (a.EntryUtc, b.EntryUtc);
				Assert.Equal (a.Label, b.Label);

				var fa = a.Features ?? Array.Empty<float> ();
				var fb = b.Features ?? Array.Empty<float> ();
				Assert.Equal (fa.Length, fb.Length);
				for (int j = 0; j < fa.Length; j++)
					Assert.Equal (fa[j], fb[j]);
				}
			}

		private static List<DataRow> BuildSyntheticRows (
			int count,
			out Dictionary<DateTime, Candle6h> sol6hDict,
			out List<Candle1m> sol1m )
			{
			var rows = new List<DataRow> (count);
			var dict6h = new Dictionary<DateTime, Candle6h> (count);
			var all1m = new List<Candle1m> (count * 30);

			var start = new DateTime (2022, 4, 1, 8, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < count; i++)
				{
				var date = start.AddDays (i);
				double price = 100 + i;

				var row = new DataRow
					{
					Date = date,
					IsMorning = true,
					MinMove = 0.03
					};
				rows.Add (row);

				dict6h[date] = new Candle6h
					{
					OpenTimeUtc = date,
					Close = price,
					High = price * 1.01,
					Low = price * 0.99
					};

				// 30 минут после entry с моментальным TP.
				for (int k = 0; k < 30; k++)
					{
					all1m.Add (new Candle1m
						{
						OpenTimeUtc = date.AddMinutes (k),
						Open = price,
						Close = price,
						High = price * 1.05, // выше TP
						Low = price * 0.95  // ниже SL
						});
					}
				}

			sol6hDict = dict6h;
			sol1m = all1m;
			return rows
				.OrderBy (r => r.Date)
				.ToList ();
			}

		private static List<DataRow> CloneRows ( List<DataRow> src )
			{
			var res = new List<DataRow> (src.Count);
			foreach (var r in src)
				{
				res.Add (new DataRow
					{
					Date = r.Date,
					IsMorning = r.IsMorning,
					MinMove = r.MinMove
					});
				}
			return res;
			}

		private static void MutateFutureTail ( List<DataRow> rows, DateTime trainUntil )
			{
			foreach (var r in rows.Where (r => r.Date > trainUntil))
				{
				r.IsMorning = !r.IsMorning;
				r.MinMove = r.MinMove * 2.0;
				}
			}
		}
	}
