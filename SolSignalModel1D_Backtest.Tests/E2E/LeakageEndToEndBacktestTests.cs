using Microsoft.ML;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.ML.SL;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.SL;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest.Tests.E2E
	{
	public sealed class LeakageEndToEndBacktestTests
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
		public void EndToEnd_DailyAndSl_Training_IsFutureBlind_ToOosCandleTailMutation_ByTrainBoundary ()
			{
			SyntheticCandleHistory.Build (
				days: 600,
				out var solWinTrainA,
				out var btcWinTrainA,
				out var paxgWinTrainA,
				out var solAll6hA,
				out var solAll1mA,
				out var sol6hDictA,
				out var fngHistory,
				out var dxyHistory);

			SyntheticCandleHistory.Clone (
				solWinTrainA, btcWinTrainA, paxgWinTrainA,
				solAll6hA, solAll1mA,
				out var solWinTrainB,
				out var btcWinTrainB,
				out var paxgWinTrainB,
				out var solAll6hB,
				out var solAll1mB);

			var rowsAAll = RowBuilder.BuildRowsDaily (
					solWinTrain: solWinTrainA,
					btcWinTrain: btcWinTrainA,
					paxgWinTrain: paxgWinTrainA,
					solAll6h: solAll6hA,
					solAll1m: solAll1mA,
					fngHistory: fngHistory,
					dxySeries: dxyHistory,
					extraDaily: null,
					nyTz: Windowing.NyTz)
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			Assert.NotEmpty (rowsAAll);

			var minDate = rowsAAll.First ().Date;
			var maxDate = rowsAAll.Last ().Date;
			var cutoffTicks = minDate.Ticks + (long) ((maxDate.Ticks - minDate.Ticks) * 0.6);
			var trainUntilUtc = new DateTime (cutoffTicks, DateTimeKind.Utc);

			var boundary = new TrainBoundary (trainUntilUtc, Windowing.NyTz);

			var tailStartUtc = trainUntilUtc.AddDays (5);

			SyntheticCandleHistory.MutateFutureTail (
				solWinTrainB, btcWinTrainB, paxgWinTrainB,
				solAll6hB, solAll1mB,
				tailStartUtc: tailStartUtc);

			var rowsBAll = RowBuilder.BuildRowsDaily (
					solWinTrain: solWinTrainB,
					btcWinTrain: btcWinTrainB,
					paxgWinTrain: paxgWinTrainB,
					solAll6h: solAll6hB,
					solAll1m: solAll1mB,
					fngHistory: fngHistory,
					dxySeries: dxyHistory,
					extraDaily: null,
					nyTz: Windowing.NyTz)
				.OrderBy (r => r.Causal.DateUtc)
				.ToList ();

			var splitA = boundary.Split (rowsAAll, r => r.Causal.DateUtc);
			var splitB = boundary.Split (rowsBAll, r => r.Causal.DateUtc);

			Assert.Empty (splitA.Excluded);
			Assert.Empty (splitB.Excluded);

			var trainRowsA = splitA.Train.OrderBy (r => r.Causal.DateUtc).ToList ();
			var trainRowsB = splitB.Train.OrderBy (r => r.Causal.DateUtc).ToList ();

			Assert.NotEmpty (trainRowsA);
			Assert.Equal (trainRowsA.Count, trainRowsB.Count);

			// Позиционный вызов: не зависит от имени trainUntil*.
			var dsA = DailyDatasetBuilder.Build (trainRowsA, trainUntilUtc, true, true, 0.7, null);
			var dsB = DailyDatasetBuilder.Build (trainRowsB, trainUntilUtc, true, true, 0.7, null);

			AssertRowsEqual (dsA.TrainRows, dsB.TrainRows);
			AssertRowsEqual (dsA.MoveTrainRows, dsB.MoveTrainRows);
			AssertRowsEqual (dsA.DirNormalRows, dsB.DirNormalRows);
			AssertRowsEqual (dsA.DirDownRows, dsB.DirDownRows);

			var bundleA = new ModelTrainer ().TrainAll (dsA.TrainRows);
			var bundleB = new ModelTrainer ().TrainAll (dsB.TrainRows);

			var movePredsA = GetMovePredictions (bundleA, dsA.MoveTrainRows);
			var movePredsB = GetMovePredictions (bundleB, dsB.MoveTrainRows);
			AssertBinaryOutputsEqual (movePredsA, movePredsB);

			var dirNormalPredsA = GetDirPredictions (bundleA, dsA.DirNormalRows);
			var dirNormalPredsB = GetDirPredictions (bundleB, dsB.DirNormalRows);
			AssertBinaryOutputsEqual (dirNormalPredsA, dirNormalPredsB);

			var dirDownPredsA = GetDirPredictions (bundleA, dsA.DirDownRows);
			var dirDownPredsB = GetDirPredictions (bundleB, dsB.DirDownRows);
			AssertBinaryOutputsEqual (dirDownPredsA, dirDownPredsB);

			const double TpPct = 0.01;
			const double SlPct = 0.02;

			var slDsA = SlDatasetBuilder.Build (trainRowsA, null, solAll1mA, sol6hDictA, trainUntilUtc, TpPct, SlPct, null);
			var slDsB = SlDatasetBuilder.Build (trainRowsB, null, solAll1mB, sol6hDictA, trainUntilUtc, TpPct, SlPct, null);

			Assert.True (slDsA.Samples.Count > 0, "E2E SL leakage test: synthetic SL dataset is empty.");
			Assert.Equal (slDsA.Samples.Count, slDsB.Samples.Count);

			var slModelA = new SlFirstTrainer ().Train (slDsA.Samples, asOfUtc: trainUntilUtc);
			var slModelB = new SlFirstTrainer ().Train (slDsB.Samples, asOfUtc: trainUntilUtc);

			var slPredsA = GetSlPredictions (slModelA, slDsA.Samples);
			var slPredsB = GetSlPredictions (slModelB, slDsB.Samples);

			AssertBinaryOutputsEqual (slPredsA, slPredsB);
			}

		private static List<BinaryOutput> GetMovePredictions ( ModelBundle bundle, IReadOnlyList<BacktestRecord> rows )
			{
			if (bundle.MoveModel == null || rows.Count == 0)
				return new List<BinaryOutput> ();

			var ml = bundle.MlCtx ?? new MLContext (seed: 42);

			var data = ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.Forward.TrueLabel != 1,
					Features = MlTrainingUtils.ToFloatFixed (r.Causal.Features)
					}));

			var scored = bundle.MoveModel.Transform (data);

			return ml.Data.CreateEnumerable<BinaryOutput> (scored, reuseRowObject: false).ToList ();
			}

		private static List<BinaryOutput> GetDirPredictions ( ModelBundle bundle, IReadOnlyList<BacktestRecord> rows )
			{
			if ((bundle.DirModelNormal == null && bundle.DirModelDown == null) || rows.Count == 0)
				return new List<BinaryOutput> ();

			var ml = bundle.MlCtx ?? new MLContext (seed: 42);

			var data = ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.Forward.TrueLabel == 2,
					Features = MlTrainingUtils.ToFloatFixed (r.Causal.Features)
					}));

			ITransformer? model = rows.Any (r => r.RegimeDown) ? bundle.DirModelDown : bundle.DirModelNormal;
			if (model == null)
				return new List<BinaryOutput> ();

			var scored = model.Transform (data);

			return ml.Data.CreateEnumerable<BinaryOutput> (scored, reuseRowObject: false).ToList ();
			}

		private static List<BinaryOutput> GetSlPredictions ( ITransformer model, IReadOnlyList<SlHitSample> samples )
			{
			var ml = new MLContext (seed: 42);

			var data = ml.Data.LoadFromEnumerable (
				samples.Select (s => new SlEvalRow
					{
					Label = s.Forward.TrueLabel,
					Features = s.Causal.Features ?? Array.Empty<float> ()
					}));

			var scored = model.Transform (data);

			return ml.Data.CreateEnumerable<BinaryOutput> (scored, reuseRowObject: false).ToList ();
			}

		private static void AssertBinaryOutputsEqual ( IReadOnlyList<BinaryOutput> a, IReadOnlyList<BinaryOutput> b, double tol = 1e-6 )
			{
			Assert.Equal (a.Count, b.Count);

			for (int i = 0; i < a.Count; i++)
				{
				Assert.Equal (a[i].PredictedLabel, b[i].PredictedLabel);
				Assert.InRange (Math.Abs (a[i].Score - b[i].Score), 0.0, tol);
				Assert.InRange (Math.Abs (a[i].Probability - b[i].Probability), 0.0, tol);
				}
			}

		private static void AssertRowsEqual ( IReadOnlyList<BacktestRecord> xs, IReadOnlyList<BacktestRecord> ys )
			{
			Assert.Equal (xs.Count, ys.Count);

			for (int i = 0; i < xs.Count; i++)
				{
				var a = xs[i];
				var b = ys[i];

				Assert.Equal (a.Causal.DateUtc, b.Causal.DateUtc);
				Assert.Equal (a.Forward.TrueLabel, b.Forward.TrueLabel);
				Assert.Equal (a.Causal.IsMorning, b.Causal.IsMorning);
				Assert.Equal (a.MinMove, b.MinMove);
				Assert.Equal (a.RegimeDown, b.RegimeDown);

				var fa = a.Causal.Features ?? Array.Empty<double> ();
				var fb = b.Causal.Features ?? Array.Empty<double> ();

				Assert.Equal (fa.Length, fb.Length);
				for (int j = 0; j < fa.Length; j++)
					Assert.Equal (fa[j], fb[j]);
				}
			}
		}
	}
