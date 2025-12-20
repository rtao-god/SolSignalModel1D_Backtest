using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Daily
	{
	/// <summary>
	/// Утечка на уровне обучения дневных моделей:
	/// изменение хвоста (DateUtc > trainUntilUtc) не меняет предсказания на train-наборе.
	/// </summary>
	public sealed class LeakageDailyModelTrainingTests
		{
		private sealed class BinaryOutput
			{
			public bool PredictedLabel { get; set; }
			public float Score { get; set; }
			public float Probability { get; set; }
			}

		[Fact]
		public void DailyMoveAndDir_Training_IsFutureBlind_ToTailMutation ()
			{
			var allRows = BuildSyntheticRows (count: 420);

			const int HoldoutDays = 120;
			var maxDateUtc = allRows[^1].DateUtc;
			var trainUntilUtc = maxDateUtc.AddDays (-HoldoutDays);

			Assert.Contains (allRows, r => r.DateUtc > trainUntilUtc);

			var rowsA = CloneRows (allRows);
			var rowsB = MutateFutureTail (CloneRows (allRows), trainUntilUtc);

			var dsA = DailyDatasetBuilder.Build (
				allRows: rowsA,
				trainUntil: trainUntilUtc,
				balanceMove: true,
				balanceDir: true,
				balanceTargetFrac: 0.7,
				datesToExclude: null);

			var dsB = DailyDatasetBuilder.Build (
				allRows: rowsB,
				trainUntil: trainUntilUtc,
				balanceMove: true,
				balanceDir: true,
				balanceTargetFrac: 0.7,
				datesToExclude: null);

			AssertRowsEqual (dsA.TrainRows, dsB.TrainRows);
			AssertRowsEqual (dsA.MoveTrainRows, dsB.MoveTrainRows);
			AssertRowsEqual (dsA.DirNormalRows, dsB.DirNormalRows);
			AssertRowsEqual (dsA.DirDownRows, dsB.DirDownRows);

			var trainerA = new ModelTrainer ();
			var bundleA = trainerA.TrainAll (dsA.TrainRows);

			var trainerB = new ModelTrainer ();
			var bundleB = trainerB.TrainAll (dsB.TrainRows);

			var movePredsA = GetMovePredictions (bundleA, dsA.MoveTrainRows);
			var movePredsB = GetMovePredictions (bundleB, dsB.MoveTrainRows);
			AssertBinaryOutputsEqual (movePredsA, movePredsB);

			var dirNormalPredsA = GetDirPredictions (bundleA, dsA.DirNormalRows);
			var dirNormalPredsB = GetDirPredictions (bundleB, dsB.DirNormalRows);
			AssertBinaryOutputsEqual (dirNormalPredsA, dirNormalPredsB);

			var dirDownPredsA = GetDirPredictions (bundleA, dsA.DirDownRows);
			var dirDownPredsB = GetDirPredictions (bundleB, dsB.DirDownRows);
			AssertBinaryOutputsEqual (dirDownPredsA, dirDownPredsB);
			}

		private static List<LabeledCausalRow> BuildSyntheticRows ( int count )
			{
			var rows = new List<LabeledCausalRow> (count);
			var start = new DateTime (2024, 1, 1, 13, 0, 0, DateTimeKind.Utc);

			var rng = new Random (123);

			for (int i = 0; i < count; i++)
				{
				var dateUtc = start.AddDays (i);

				double solRet1 = (rng.NextDouble () - 0.5) * 0.10;

				int label =
					solRet1 > 0.01 ? 2 :
					solRet1 < -0.01 ? 0 :
					1;

				bool regimeDown = (label == 0);

				bool factMicroUp = false;
				bool factMicroDown = false;

				if (label == 1)
					{
					factMicroUp = rng.NextDouble () < 0.5;
					factMicroDown = !factMicroUp;
					}

				var causal = CreateCausal (
					dateUtc: dateUtc,
					regimeDown: regimeDown,
					solRet1: solRet1);

				rows.Add (new LabeledCausalRow (
					causal: causal,
					trueLabel: label,
					factMicroUp: factMicroUp,
					factMicroDown: factMicroDown));
				}

			return rows.OrderBy (r => r.DateUtc).ToList ();
			}

		private static CausalDataRow CreateCausal ( DateTime dateUtc, bool regimeDown, double solRet1 )
			{
			return new CausalDataRow (
				dateUtc: dateUtc,
				regimeDown: regimeDown,
				isMorning: true,
				hardRegime: 1,
				minMove: 0.02,

				solRet30: solRet1 * 0.2,
				btcRet30: 0.0,
				solBtcRet30: 0.0,

				solRet1: solRet1,
				solRet3: solRet1 * 0.7,
				btcRet1: solRet1 * 0.1,
				btcRet3: solRet1 * 0.05,

				fngNorm: 0.0,
				dxyChg30: 0.0,
				goldChg30: 0.0,

				btcVs200: 0.0,

				solRsiCenteredScaled: 0.0,
				rsiSlope3Scaled: 0.0,

				gapBtcSol1: 0.0,
				gapBtcSol3: 0.0,

				atrPct: 0.02,
				dynVol: 1.0,

				solAboveEma50: 1.0,
				solEma50vs200: 0.01,
				btcEma50vs200: 0.01);
			}

		private static List<LabeledCausalRow> CloneRows ( List<LabeledCausalRow> src )
			{
			var res = new List<LabeledCausalRow> (src.Count);

			foreach (var r in src)
				{
				var c = r.Causal;

				var clonedCausal = new CausalDataRow (
					dateUtc: c.DateUtc,
					regimeDown: c.RegimeDown,
					isMorning: c.IsMorning,
					hardRegime: c.HardRegime,
					minMove: c.MinMove,

					solRet30: c.SolRet30,
					btcRet30: c.BtcRet30,
					solBtcRet30: c.SolBtcRet30,

					solRet1: c.SolRet1,
					solRet3: c.SolRet3,
					btcRet1: c.BtcRet1,
					btcRet3: c.BtcRet3,

					fngNorm: c.FngNorm,
					dxyChg30: c.DxyChg30,
					goldChg30: c.GoldChg30,

					btcVs200: c.BtcVs200,

					solRsiCenteredScaled: c.SolRsiCenteredScaled,
					rsiSlope3Scaled: c.RsiSlope3Scaled,

					gapBtcSol1: c.GapBtcSol1,
					gapBtcSol3: c.GapBtcSol3,

					atrPct: c.AtrPct,
					dynVol: c.DynVol,

					solAboveEma50: c.SolAboveEma50,
					solEma50vs200: c.SolEma50vs200,
					btcEma50vs200: c.BtcEma50vs200);

				res.Add (new LabeledCausalRow (
					causal: clonedCausal,
					trueLabel: r.TrueLabel,
					factMicroUp: r.FactMicroUp,
					factMicroDown: r.FactMicroDown));
				}

			return res;
			}

		private static List<LabeledCausalRow> MutateFutureTail ( List<LabeledCausalRow> rows, DateTime trainUntilUtc )
			{
			var res = new List<LabeledCausalRow> (rows.Count);

			foreach (var r in rows)
				{
				if (r.DateUtc <= trainUntilUtc)
					{
					res.Add (r);
					continue;
					}

				var c = r.Causal;

				var mutatedCausal = new CausalDataRow (
					dateUtc: c.DateUtc,
					regimeDown: !c.RegimeDown,
					isMorning: c.IsMorning,
					hardRegime: c.HardRegime,
					minMove: c.MinMove * 2.0,

					solRet30: 10_000.0,
					btcRet30: 10_001.0,
					solBtcRet30: 10_002.0,

					solRet1: 10_003.0,
					solRet3: 10_004.0,
					btcRet1: 10_005.0,
					btcRet3: 10_006.0,

					fngNorm: 10_007.0,
					dxyChg30: 10_008.0,
					goldChg30: 10_009.0,

					btcVs200: 10_010.0,

					solRsiCenteredScaled: 10_011.0,
					rsiSlope3Scaled: 10_012.0,

					gapBtcSol1: 10_013.0,
					gapBtcSol3: 10_014.0,

					atrPct: 10_015.0,
					dynVol: 10_016.0,

					solAboveEma50: 10_017.0,
					solEma50vs200: 10_018.0,
					btcEma50vs200: 10_019.0);

				res.Add (new LabeledCausalRow (
					causal: mutatedCausal,
					trueLabel: 2,
					factMicroUp: false,
					factMicroDown: false));
				}

			return res;
			}

		private static void AssertRowsEqual ( IReadOnlyList<LabeledCausalRow> xs, IReadOnlyList<LabeledCausalRow> ys )
			{
			Assert.Equal (xs.Count, ys.Count);

			for (int i = 0; i < xs.Count; i++)
				{
				var a = xs[i];
				var b = ys[i];

				Assert.Equal (a.DateUtc, b.DateUtc);
				Assert.Equal (a.TrueLabel, b.TrueLabel);
				Assert.Equal (a.Causal.RegimeDown, b.Causal.RegimeDown);

				var va = a.Causal.FeaturesVector;
				var vb = b.Causal.FeaturesVector;

				Assert.Equal (va.Length, vb.Length);

				var sa = va.Span;
				var sb = vb.Span;

				for (int j = 0; j < sa.Length; j++)
					Assert.Equal (sa[j], sb[j]);
				}
			}

		private static List<BinaryOutput> GetMovePredictions ( ModelBundle bundle, IReadOnlyList<LabeledCausalRow> rows )
			{
			if (bundle.MoveModel == null || rows.Count == 0)
				return new List<BinaryOutput> ();

			var ml = bundle.MlCtx ?? new MLContext (seed: 42);

			var data = ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.TrueLabel != 1,
					Features = MlTrainingUtils.ToFloatFixed (r.Causal.FeaturesVector)
					}));

			var scored = bundle.MoveModel.Transform (data);

			return ml.Data.CreateEnumerable<BinaryOutput> (scored, reuseRowObject: false).ToList ();
			}

		private static List<BinaryOutput> GetDirPredictions ( ModelBundle bundle, IReadOnlyList<LabeledCausalRow> rows )
			{
			if ((bundle.DirModelNormal == null && bundle.DirModelDown == null) || rows.Count == 0)
				return new List<BinaryOutput> ();

			var ml = bundle.MlCtx ?? new MLContext (seed: 42);

			var data = ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.TrueLabel == 2,
					Features = MlTrainingUtils.ToFloatFixed (r.Causal.FeaturesVector)
					}));

			bool isDownSubset = rows.Any (r => r.Causal.RegimeDown);
			var model = isDownSubset ? bundle.DirModelDown : bundle.DirModelNormal;

			if (model == null)
				return new List<BinaryOutput> ();

			var scored = model.Transform (data);

			return ml.Data.CreateEnumerable<BinaryOutput> (scored, reuseRowObject: false).ToList ();
			}

		private static void AssertBinaryOutputsEqual ( IReadOnlyList<BinaryOutput> a, IReadOnlyList<BinaryOutput> b )
			{
			Assert.Equal (a.Count, b.Count);

			for (int i = 0; i < a.Count; i++)
				{
				Assert.Equal (a[i].PredictedLabel, b[i].PredictedLabel);
				Assert.Equal (a[i].Score, b[i].Score, 6);
				Assert.Equal (a[i].Probability, b[i].Probability, 6);
				}
			}
		}
	}
