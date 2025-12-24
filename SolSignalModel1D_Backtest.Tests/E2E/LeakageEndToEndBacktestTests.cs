using Microsoft.ML;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

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

		[Fact]
		public void EndToEnd_DailyTraining_IsFutureBlind_ToOosCandleTailMutation_ByTrainUntil ()
			{
			SyntheticCandleHistory.Build (
				days: 600,
				out var solWinTrainA,
				out var btcWinTrainA,
				out var paxgWinTrainA,
				out var solAll6hA,
				out var solAll1hA,
				out var solAll1mA,
				out var sol6hDictA,
				out var fngHistory,
				out var dxyHistory);

			SyntheticCandleHistory.Clone (
				solWinTrainA, btcWinTrainA, paxgWinTrainA,
				solAll6hA, solAll1hA, solAll1mA,
				out var solWinTrainB,
				out var btcWinTrainB,
				out var paxgWinTrainB,
				out var solAll6hB,
				out var solAll1hB,
				out var solAll1mB);

			var buildA0 = RowBuilder.BuildDailyRows (
				solWinTrain: solWinTrainA,
				btcWinTrain: btcWinTrainA,
				paxgWinTrain: paxgWinTrainA,
				solAll6h: solAll6hA,
				solAll1m: solAll1mA,
				fngHistory: fngHistory,
				dxySeries: dxyHistory,
				extraDaily: null,
				nyTz: NyWindowing.NyTz);

			var rowsAAll = buildA0.LabeledRows
				.OrderBy (r => r.DateUtc)
				.ToList ();

			Assert.NotEmpty (rowsAAll);

			var minDate = rowsAAll.First ().DateUtc;
			var maxDate = rowsAAll.Last ().DateUtc;

			var cutoffTicks = minDate.Ticks + (long) ((maxDate.Ticks - minDate.Ticks) * 0.60);
			var trainUntilUtc = new DateTime (cutoffTicks, DateTimeKind.Utc);

			var tailStartUtc = trainUntilUtc.AddDays (5);

			SyntheticCandleHistory.MutateFutureTail (
				solWinTrain: solWinTrainB,
				btcWinTrain: btcWinTrainB,
				paxgWinTrain: paxgWinTrainB,
				solAll6h: solAll6hB,
				solAll1h: solAll1hB,
				solAll1m: solAll1mB,
				tailStartUtc: tailStartUtc);

			var buildB0 = RowBuilder.BuildDailyRows (
				solWinTrain: solWinTrainB,
				btcWinTrain: btcWinTrainB,
				paxgWinTrain: paxgWinTrainB,
				solAll6h: solAll6hB,
				solAll1m: solAll1mB,
				fngHistory: fngHistory,
				dxySeries: dxyHistory,
				extraDaily: null,
				nyTz: NyWindowing.NyTz);

			var rowsBAll = buildB0.LabeledRows
				.OrderBy (r => r.DateUtc)
				.ToList ();

			Assert.NotEmpty (rowsBAll);

			var dsA = DailyDatasetBuilder.Build (
				allRows: rowsAAll,
				trainUntil: trainUntilUtc,
				balanceMove: true,
				balanceDir: true,
				balanceTargetFrac: 0.7,
				datesToExclude: null);

			var dsB = DailyDatasetBuilder.Build (
				allRows: rowsBAll,
				trainUntil: trainUntilUtc,
				balanceMove: true,
				balanceDir: true,
				balanceTargetFrac: 0.7,
				datesToExclude: null);

			AssertLabeledRowsEqual (dsA.TrainRows, dsB.TrainRows);
			AssertLabeledRowsEqual (dsA.MoveTrainRows, dsB.MoveTrainRows);
			AssertLabeledRowsEqual (dsA.DirNormalRows, dsB.DirNormalRows);
			AssertLabeledRowsEqual (dsA.DirDownRows, dsB.DirDownRows);

			var bundleA = new ModelTrainer ().TrainAll (dsA.TrainRows);
			var bundleB = new ModelTrainer ().TrainAll (dsB.TrainRows);

			var movePredsA = GetBinaryPredictions (
				bundleA.MlCtx,
				bundleA.MoveModel,
				dsA.MoveTrainRows,
				labelSelector: r => r.TrueLabel != 1);

			var movePredsB = GetBinaryPredictions (
				bundleB.MlCtx,
				bundleB.MoveModel,
				dsB.MoveTrainRows,
				labelSelector: r => r.TrueLabel != 1);

			AssertBinaryOutputsEqual (movePredsA, movePredsB);

			var dirNormalPredsA = GetBinaryPredictions (
				bundleA.MlCtx,
				bundleA.DirModelNormal,
				dsA.DirNormalRows,
				labelSelector: r => r.TrueLabel == 2);

			var dirNormalPredsB = GetBinaryPredictions (
				bundleB.MlCtx,
				bundleB.DirModelNormal,
				dsB.DirNormalRows,
				labelSelector: r => r.TrueLabel == 2);

			AssertBinaryOutputsEqual (dirNormalPredsA, dirNormalPredsB);

			var dirDownPredsA = GetBinaryPredictions (
				bundleA.MlCtx,
				bundleA.DirModelDown,
				dsA.DirDownRows,
				labelSelector: r => r.TrueLabel == 2);

			var dirDownPredsB = GetBinaryPredictions (
				bundleB.MlCtx,
				bundleB.DirModelDown,
				dsB.DirDownRows,
				labelSelector: r => r.TrueLabel == 2);

			AssertBinaryOutputsEqual (dirDownPredsA, dirDownPredsB);
			}

		private static List<BinaryOutput> GetBinaryPredictions (
			MLContext ml,
			ITransformer? model,
			IReadOnlyList<LabeledCausalRow> rows,
			Func<LabeledCausalRow, bool> labelSelector )
			{
			if (model == null || rows.Count == 0)
				return new List<BinaryOutput> ();

			var data = ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = labelSelector (r),
					Features = MlTrainingUtils.ToFloatFixed (r.Causal.FeaturesVector)
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

		private static void AssertLabeledRowsEqual ( IReadOnlyList<LabeledCausalRow> xs, IReadOnlyList<LabeledCausalRow> ys )
			{
			Assert.Equal (xs.Count, ys.Count);

			for (int i = 0; i < xs.Count; i++)
				{
				var a = xs[i];
				var b = ys[i];

				Assert.Equal (a.DateUtc, b.DateUtc);
				Assert.Equal (a.TrueLabel, b.TrueLabel);
				Assert.Equal (a.FactMicroUp, b.FactMicroUp);
				Assert.Equal (a.FactMicroDown, b.FactMicroDown);

				var va = a.Causal.FeaturesVector.Span;
				var vb = b.Causal.FeaturesVector.Span;

				Assert.Equal (va.Length, vb.Length);
				for (int j = 0; j < va.Length; j++)
					Assert.Equal (va[j], vb[j], precision: 10);

				Assert.Equal (a.Causal.RegimeDown, b.Causal.RegimeDown);
				Assert.Equal (a.Causal.IsMorning, b.Causal.IsMorning);
				Assert.Equal (a.Causal.MinMove, b.Causal.MinMove, precision: 12);
				}
			}
		}
	}
