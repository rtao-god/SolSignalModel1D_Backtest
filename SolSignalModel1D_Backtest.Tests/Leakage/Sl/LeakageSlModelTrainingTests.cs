using Microsoft.ML;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Omniscient.ML.SL;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.ML.SL;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Data;

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

			[VectorType (SlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[SlSchema.FeatureCount];
			}

		[Fact]
		public void SlModel_Training_IsFutureBlind_ToTailMutation ()
			{
			var allRows = BuildSyntheticRows (
				count: 40,
				out var sol6hDict,
				out var sol1h,
				out var sol1m);

			var maxDateUtc = allRows.Last ().EntryUtc.Value;
			var trainUntil = maxDateUtc.AddDays (-10);
			var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromExitDayKeyUtc (
				NyWindowing.ComputeExitDayKeyUtc (
					new EntryUtc (trainUntil),
					NyWindowing.NyTz));

			var rowsA = CloneRows (allRows);
			var rowsB = MutateFutureTail (CloneRows (allRows), trainUntilExitDayKeyUtc);

			var dsA = SlDatasetBuilder.Build (
				rows: rowsA,
				sol1h: sol1h,
				sol1m: sol1m,
				sol6hDict: sol6hDict,
				trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
				tpPct: 0.03,
				slPct: 0.05,
				strongSelector: null);

			var dsB = SlDatasetBuilder.Build (
				rows: rowsB,
				sol1h: sol1h,
				sol1m: sol1m,
				sol6hDict: sol6hDict,
				trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
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
			out List<Candle1h> sol1h,
			out List<Candle1m> sol1m )
			{
			var rows = new List<BacktestRecord> (count);
			var dict6h = new Dictionary<DateTime, Candle6h> (count);
			var all1m = new List<Candle1m> (count * 20);

			var entriesUtc = NyTestDates.BuildNyWeekdaySeriesUtc (
				startNyLocalDate: NyTestDates.NyLocal (2022, 4, 1, 0),
				count: count,
				hour: 8);

			for (var i = 0; i < count; i++)
				{
				var dateUtc = entriesUtc[i];
				var price = 100 + i;
				bool makeTp = (i % 2 == 0);

				rows.Add (CreateBacktestRecord (
					dateUtc: dateUtc,
					isMorning: true,
					minMove: 0.03));

				dict6h[dateUtc] = new Candle6h
					{
					OpenTimeUtc = dateUtc,
					Open = price,
					Close = price,
					High = price * 1.02,
					Low = price * 0.98
					};

				for (var k = 0; k < 20; k++)
					{
					double high = makeTp ? price * 1.04 : price * 1.02;
					double low = makeTp ? price * 0.97 : price * 0.94;

					all1m.Add (new Candle1m
						{
						OpenTimeUtc = dateUtc.AddMinutes (k),
						Open = price,
						Close = price,
						High = high,
						Low = low
						});
					}
				}

			sol6hDict = dict6h;
			sol1h = BuildHourlySeries (entriesUtc, basePrice: 100.0);
			sol1m = all1m;

			return rows.OrderBy (r => r.EntryUtc.Value).ToList ();
			}

		private static BacktestRecord CreateBacktestRecord ( DateTime dateUtc, bool isMorning, double minMove )
			{
			var vec = BuildVector64Deterministic (dateUtc);
			var nyEntryUtc = NyWindowing.CreateNyTradingEntryUtcOrThrow (new EntryUtc (dateUtc), NyWindowing.NyTz);

			var causal = new CausalPredictionRecord
				{
				TradingEntryUtc = nyEntryUtc,
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
				EntryUtc = new EntryUtc (dateUtc),
				WindowEndUtc = NyWindowing.ComputeBaselineExitUtc (new EntryUtc (dateUtc), NyWindowing.NyTz).Value,
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
					dateUtc: r.EntryUtc.Value,
					isMorning: r.Causal.IsMorning == true,
					minMove: r.MinMove));
				}
			return res;
			}

		private static List<BacktestRecord> MutateFutureTail ( List<BacktestRecord> rows, TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc )
			{
			var res = new List<BacktestRecord> (rows.Count);

			foreach (var r in rows)
				{
				var cls = NyTrainSplit.ClassifyByBaselineExit (
					entryUtc: r.EntryUtc,
					trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
					nyTz: NyWindowing.NyTz,
					baselineExitDayKeyUtc: out _);

				if (cls == NyTrainSplit.EntryClass.Train)
					{
					res.Add (r);
					continue;
					}

				res.Add (CreateBacktestRecord (
					dateUtc: r.EntryUtc.Value,
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

				Assert.Equal (a.EntryUtc.Value, b.EntryUtc.Value);
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

					if (s.Features.Length != SlSchema.FeatureCount)
						{
						throw new InvalidOperationException (
							$"[sl-test] SlHitSample.Features length mismatch: got={s.Features.Length}, expected={SlSchema.FeatureCount}.");
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

		private static List<Candle1h> BuildHourlySeries ( IReadOnlyList<DateTime> entriesUtc, double basePrice )
			{
			if (entriesUtc.Count == 0)
				return new List<Candle1h> ();

			var first = entriesUtc[0].AddHours (-24);
			var last = entriesUtc[^1].AddHours (1);

			var list = new List<Candle1h> ();
			for (var t = first; t <= last; t = t.AddHours (1))
				{
				list.Add (new Candle1h
					{
					OpenTimeUtc = t,
					Open = basePrice,
					Close = basePrice,
					High = basePrice * 1.01,
					Low = basePrice * 0.99
					});
				}

			return list;
			}
		}
	}
