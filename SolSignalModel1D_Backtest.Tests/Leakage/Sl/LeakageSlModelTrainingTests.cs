using Microsoft.ML;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.SL;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.SL;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage
	{
	/// <summary>
	/// Утечка на уровне обучения SL-модели (SlFirstTrainer):
	/// изменение хвоста (DateUtc > trainUntil) не должно влиять на train-часть.
	/// </summary>
	public sealed class LeakageSlModelTrainingTests
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
			var allRows = BuildSyntheticRows (
				count: 40,
				out var sol6hDict,
				out var sol1m);

			var maxDateUtc = allRows.Last ().DateUtc;
			var trainUntil = maxDateUtc.AddDays (-10);

			var rowsA = CloneRows (allRows);
			var rowsB = MutateFutureTail (CloneRows (allRows), trainUntil);

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

			var trainerA = new SlFirstTrainer ();
			var modelA = trainerA.Train (dsA.Samples, asOfUtc: trainUntil);

			var trainerB = new SlFirstTrainer ();
			var modelB = trainerB.Train (dsB.Samples, asOfUtc: trainUntil);

			var predsA = GetSlPredictions (modelA, dsA.Samples);
			var predsB = GetSlPredictions (modelB, dsB.Samples);

			AssertBinaryOutputsEqual (predsA, predsB);
			}

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
				var dateUtc = start.AddDays (i);
				var price = 100 + i;

				rows.Add (CreateBacktestRecord (
					dateUtc: dateUtc,
					isMorning: true,
					minMove: 0.03));

				dict6h[dateUtc] = new Candle6h
					{
					OpenTimeUtc = dateUtc,
					Close = price,
					High = price * 1.02,
					Low = price * 0.98
					};

				for (var k = 0; k < 20; k++)
					{
					all1m.Add (new Candle1m
						{
						OpenTimeUtc = dateUtc.AddMinutes (k),
						Open = price,
						Close = price,
						High = price * 1.05,
						Low = price * 0.95
						});
					}
				}

			sol6hDict = dict6h;
			sol1m = all1m;

			return rows.OrderBy (r => r.DateUtc).ToList ();
			}

		private static BacktestRecord CreateBacktestRecord ( DateTime dateUtc, bool isMorning, double minMove )
			{
			var vec = BuildVector64Deterministic (dateUtc);

			var causal = new CausalPredictionRecord
				{
				DateUtc = dateUtc,
				FeaturesVector = vec,
				Features = new CausalFeatures { IsMorning = isMorning },
				PredLabel = 1,
				PredLabel_Day = 1,
				PredLabel_DayMicro = 1,
				PredLabel_Total = 1,
				ProbUp_Day = 0.0,
				ProbFlat_Day = 1.0,
				ProbDown_Day = 0.0,
				ProbUp_DayMicro = 0.0,
				ProbFlat_DayMicro = 1.0,
				ProbDown_DayMicro = 0.0,
				ProbUp_Total = 0.0,
				ProbFlat_Total = 1.0,
				ProbDown_Total = 0.0,
				Conf_Day = 1.0,
				Conf_Micro = 0.0,
				MicroPredicted = false,
				PredMicroUp = false,
				PredMicroDown = false,
				RegimeDown = false,
				Reason = string.Empty,
				MinMove = minMove
				};

			var forward = new ForwardOutcomes
				{
				DateUtc = dateUtc,
				WindowEndUtc = dateUtc.AddHours (24),
				Entry = 100.0,
				MaxHigh24 = 105.0,
				MinLow24 = 95.0,
				Close24 = 100.0,
				DayMinutes = Array.Empty<Candle1m> (),
				MinMove = minMove,
				TrueLabel = 1,
				FactMicroUp = false,
				FactMicroDown = false
				};

			return new BacktestRecord
				{
				Causal = causal,
				Forward = forward
				};
			}

		private static double[] BuildVector64Deterministic ( DateTime dateUtc )
			{
			var v = new double[MlSchema.FeatureCount];

			int seed = dateUtc.Year * 10_000 + dateUtc.Month * 100 + dateUtc.Day;
			var rng = new Random (seed);

			for (int i = 0; i < v.Length; i++)
				v[i] = (rng.NextDouble () - 0.5) * 2.0;

			return v;
			}

		private static List<BacktestRecord> CloneRows ( List<BacktestRecord> src )
			{
			var res = new List<BacktestRecord> (src.Count);
			foreach (var r in src)
				{
				res.Add (CreateBacktestRecord (
					dateUtc: r.DateUtc,
					isMorning: r.Causal.IsMorning == true,
					minMove: r.MinMove));
				}
			return res;
			}

		private static List<BacktestRecord> MutateFutureTail ( List<BacktestRecord> rows, DateTime trainUntilUtc )
			{
			var res = new List<BacktestRecord> (rows.Count);

			foreach (var r in rows)
				{
				if (r.DateUtc <= trainUntilUtc)
					{
					res.Add (r);
					continue;
					}

				res.Add (CreateBacktestRecord (
					dateUtc: r.DateUtc,
					isMorning: !(r.Causal.IsMorning == true),
					minMove: r.MinMove * 2.0));
				}

			return res;
			}

		private static void AssertRowsEqual ( List<BacktestRecord> xs, List<BacktestRecord> ys )
			{
			Assert.Equal (xs.Count, ys.Count);

			for (int i = 0; i < xs.Count; i++)
				{
				var a = xs[i];
				var b = ys[i];

				Assert.Equal (a.DateUtc, b.DateUtc);
				Assert.Equal (a.Causal.IsMorning, b.Causal.IsMorning);
				Assert.Equal (a.MinMove, b.MinMove);
				}
			}

		private static List<BinaryOutput> GetSlPredictions ( ITransformer model, List<SlHitSample> samples )
			{
			if (model == null) throw new ArgumentNullException (nameof (model));
			if (samples == null) throw new ArgumentNullException (nameof (samples));

			var ml = new MLContext (seed: 42);

			var data = ml.Data.LoadFromEnumerable (
				samples.Select (s =>
				{
					if (s.Features == null)
						throw new InvalidOperationException ("[sl-test] SlHitSample.Features is null.");

					if (s.Features.Length != MlSchema.FeatureCount)
						{
						throw new InvalidOperationException (
							$"[sl-test] SlHitSample.Features length mismatch: got={s.Features.Length}, expected={MlSchema.FeatureCount}.");
						}

					return new SlEvalRow
						{
						Label = s.Label,
						Features = s.Features
						};
				}));

			var scored = model.Transform (data);

			return ml.Data
				.CreateEnumerable<BinaryOutput> (scored, reuseRowObject: false)
				.ToList ();
			}

		private static void AssertBinaryOutputsEqual ( List<BinaryOutput> a, List<BinaryOutput> b, double tol = 1e-6 )
			{
			Assert.Equal (a.Count, b.Count);

			for (int i = 0; i < a.Count; i++)
				{
				Assert.Equal (a[i].PredictedLabel, b[i].PredictedLabel);
				Assert.InRange (Math.Abs (a[i].Score - b[i].Score), 0.0, tol);
				Assert.InRange (Math.Abs (a[i].Probability - b[i].Probability), 0.0, tol);
				}
			}
		}
	}
