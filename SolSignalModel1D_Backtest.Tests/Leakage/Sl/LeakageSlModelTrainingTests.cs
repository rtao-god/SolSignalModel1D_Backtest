using Microsoft.ML;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.SL;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.SL;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage
	{
	/// <summary>
	/// Тесты утечки на уровне обучения SL-модели (SlFirstTrainer).
	/// Предполагается, что SlDatasetBuilder уже future-blind.
	/// Здесь проверяется, что изменение будущего хвоста не меняет
	/// предсказания модели на train-сэмплах SL.
	/// </summary>
	public class LeakageSlModelTrainingTests
		{
		private sealed class BinaryOutput
			{
			public bool PredictedLabel { get; set; }
			public float Score { get; set; }
			public float Probability { get; set; }
			}

		private sealed class SlEvalRow
			{
			public bool Label { get; set; }

			[VectorType (MlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[MlSchema.FeatureCount];
			}

		[Fact]
		public void SlModel_Training_IsFutureBlind_ToTailMutation ()
			{
			// 1. Синтетические BacktestRecord + свечи для SL-датасета.
			var allRows = BuildSyntheticRows (
				count: 40,
				out var sol6hDict,
				out var sol1m);

			var maxDate = allRows.Last ().Date;
			var trainUntil = maxDate.AddDays (-10);

			var rowsA = CloneRows (allRows);
			var rowsB = CloneRows (allRows);

			MutateFutureTail (rowsB, trainUntil);

			// 2. Датасеты SL A/B.
			var dsA = SlDatasetBuilder.Build (
				rows: rowsA,
				sol1h: null,
				sol1m: sol1m,
				sol6hDict: sol6hDict,
				trainUntil: trainUntil,
				tpPct: 0.03,
				slPct: 0.05,
				strongSelector: null);

			var dsB = SlDatasetBuilder.Build (
				rows: rowsB,
				sol1h: null,
				sol1m: sol1m,
				sol6hDict: sol6hDict,
				trainUntil: trainUntil,
				tpPct: 0.03,
				slPct: 0.05,
				strongSelector: null);

			AssertRowsEqual (dsA.MorningRows, dsB.MorningRows);
			Assert.Equal (dsA.Samples.Count, dsB.Samples.Count);

			Assert.True (dsA.Samples.Count > 0, "Synthetic SL dataset is empty in leakage test.");

			// 3. Обучаем SL-модели на sample-ах A/B.
			var trainerA = new SlFirstTrainer ();
			var modelA = trainerA.Train (
				samples: dsA.Samples,
				asOfUtc: trainUntil);

			var trainerB = new SlFirstTrainer ();
			var modelB = trainerB.Train (
				samples: dsB.Samples,
				asOfUtc: trainUntil);

			// 4. Сравниваем предсказания на одних и тех же SL-сэмплах.
			var predsA = GetSlPredictions (modelA, dsA.Samples);
			var predsB = GetSlPredictions (modelB, dsB.Samples);

			AssertBinaryOutputsEqual (predsA, predsB);
			}

		// === helpers ===

		private static List<BacktestRecord> BuildSyntheticRows (
			int count,
			out Dictionary<DateTime, Candle6h> sol6hDict,
			out List<Candle1m> sol1m )
			{
			var rows = new List<BacktestRecord> (count);
			var dict6h = new Dictionary<DateTime, Candle6h> (count);
			var all1m = new List<Candle1m> (count * 20);

			var start = new DateTime (2022, 4, 1, 8, 0, 0, DateTimeKind.Utc);

			for (var i = 0; i < count; i++)
				{
				var date = start.AddDays (i);
				var price = 100 + i;

				var row = new BacktestRecord
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
					High = price * 1.02,
					Low = price * 0.98
					};

				// 20 минут будущего: и TP, и SL потенциально достижимы.
				for (var k = 0; k < 20; k++)
					{
					all1m.Add (new Candle1m
						{
						OpenTimeUtc = date.AddMinutes (k),
						Open = price,
						Close = price,
						High = price * 1.05,
						Low = price * 0.95
						});
					}
				}

			sol6hDict = dict6h;
			sol1m = all1m;
			return rows.OrderBy (r => r.ToCausalDateUtc()).ToList ();
			}

		private static List<BacktestRecord> CloneRows ( List<BacktestRecord> src )
			{
			var res = new List<BacktestRecord> (src.Count);
			foreach (var r in src)
				{
				res.Add (new BacktestRecord
					{
					Date = r.ToCausalDateUtc(),
					IsMorning = r.Causal.IsMorning,
					MinMove = r.MinMove
					});
				}
			return res;
			}

		private static void MutateFutureTail ( List<BacktestRecord> rows, DateTime trainUntil )
			{
			foreach (var r in rows.Where (r => r.ToCausalDateUtc() > trainUntil))
				{
				r.Causal.IsMorning = !r.Causal.IsMorning;
				r.MinMove = r.MinMove * 2.0;
				}
			}

		private static void AssertRowsEqual ( List<BacktestRecord> xs, List<BacktestRecord> ys )
			{
			Assert.Equal (xs.Count, ys.Count);

			for (var i = 0; i < xs.Count; i++)
				{
				var a = xs[i];
				var b = ys[i];

				Assert.Equal (a.ToCausalDateUtc(), b.ToCausalDateUtc());
				Assert.Equal (a.Causal.IsMorning, b.Causal.IsMorning);
				Assert.Equal (a.MinMove, b.MinMove);
				}
			}

		private static List<BinaryOutput> GetSlPredictions (
			ITransformer model,
			List<SlHitSample> samples )
			{
			var ml = new MLContext (seed: 42);

			var data = ml.Data.LoadFromEnumerable (
				samples.Select (s => new SlEvalRow
					{
					Label = s.Forward.TrueLabel,
					Features = s.Causal.Features ?? Array.Empty<float> ()
					}));

			var scored = model.Transform (data);

			return ml.Data
				.CreateEnumerable<BinaryOutput> (scored, reuseRowObject: false)
				.ToList ();
			}

		private static void AssertBinaryOutputsEqual (
			List<BinaryOutput> a,
			List<BinaryOutput> b,
			double tol = 1e-6 )
			{
			Assert.Equal (a.Count, b.Count);

			for (var i = 0; i < a.Count; i++)
				{
				Assert.Equal (a[i].PredictedLabel, b[i].PredictedLabel);
				Assert.InRange (Math.Abs (a[i].Score - b[i].Score), 0.0, tol);
				Assert.InRange (Math.Abs (a[i].Probability - b[i].Probability), 0.0, tol);
				}
			}
		}
	}
