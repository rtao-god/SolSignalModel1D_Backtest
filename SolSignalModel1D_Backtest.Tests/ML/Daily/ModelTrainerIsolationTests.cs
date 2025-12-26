using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.Daily
	{
	public sealed class ModelTrainerIsolationTests
		{
		[Fact]
		public void MoveModel_QualityDrops_WhenTrainLabelsAreShuffled ()
			{
			var rows = BuildSyntheticLabeledRows (count: 600, seed: 123);

			var trainerSignal = new ModelTrainer
				{
				DisableDirNormalModel = true,
				DisableDirDownModel = true,
				DisableMicroFlatModel = true
				};

			var bundleSignal = trainerSignal.TrainAll (rows);
			Assert.NotNull (bundleSignal.MoveModel);

			var rowsShuffled = CloneRowsWithShuffledMoveLabel (rows, seed: 42);

			var trainerShuffled = new ModelTrainer
				{
				DisableDirNormalModel = true,
				DisableDirDownModel = true,
				DisableMicroFlatModel = true
				};

			var bundleShuffled = trainerShuffled.TrainAll (rowsShuffled);
			Assert.NotNull (bundleShuffled.MoveModel);

			var ml = bundleSignal.MlCtx;

			var evalData = ml.Data.LoadFromEnumerable (
				rows.Select (r => new MlSampleBinary
					{
					Label = r.TrueLabel != 1,
					Features = MlTrainingUtils.ToFloatFixed (r.Causal.FeaturesVector)
					}));

			var predSignal = bundleSignal.MoveModel!.Transform (evalData);
			var metricsSignal = ml.BinaryClassification.Evaluate (predSignal);

			var predShuffled = bundleShuffled.MoveModel!.Transform (evalData);
			var metricsShuffled = ml.BinaryClassification.Evaluate (predShuffled);

			Assert.True (
				metricsSignal.AreaUnderRocCurve > 0.75,
				$"Expected decent AUC on structured synthetic data, got {metricsSignal.AreaUnderRocCurve:F3}");

			Assert.True (
				metricsShuffled.AreaUnderRocCurve < metricsSignal.AreaUnderRocCurve - 0.15,
				$"Expected AUC drop after shuffle, got {metricsShuffled.AreaUnderRocCurve:F3} vs {metricsSignal.AreaUnderRocCurve:F3}");
			}

		private static List<LabeledCausalRow> BuildSyntheticLabeledRows ( int count, int seed )
			{
			var rng = new Random (seed);
			var rows = new List<LabeledCausalRow> (count);

			var start = new DateTime (2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			for (int i = 0; i < count; i++)
				{
				var dateUtc = start.AddDays (i);

				double x1 = rng.NextDouble () * 2.0 - 1.0;
				double x2 = rng.NextDouble () * 2.0 - 1.0;

				double score = 0.9 * x1 + 0.6 * x2 + (rng.NextDouble () * 0.2 - 0.1);

				int trueLabel;
				if (score > 0.35) trueLabel = 2;
				else if (score < -0.35) trueLabel = 0;
				else trueLabel = 1;

				bool factMicroUp = trueLabel == 1 && score > 0;
				bool factMicroDown = trueLabel == 1 && score < 0;

				var causal = new CausalDataRow (
					dateUtc: dateUtc,
					regimeDown: score < -0.2,
					isMorning: true,
					hardRegime: 1,
					minMove: Math.Abs (score) * 0.02,

					solRet30: score * 0.05,
					btcRet30: score * 0.03,
					solBtcRet30: score * 0.02,

					solRet1: x1 * 0.01,
					solRet3: x2 * 0.02,
					btcRet1: x2 * 0.01,
					btcRet3: x1 * 0.02,

					fngNorm: x1,
					dxyChg30: Math.Clamp (x2 * 0.01, -0.03, 0.03),
					goldChg30: x2 * 0.01,

					btcVs200: x1 * 0.02,

					solRsiCenteredScaled: x1 * 0.5,
					rsiSlope3Scaled: x2 * 0.5,

					gapBtcSol1: (x2 - x1) * 0.01,
					gapBtcSol3: (x1 - x2) * 0.02,

					atrPct: 0.02 + Math.Abs (x1) * 0.01,
					dynVol: 0.5 + Math.Abs (x2) * 0.5,

					solAboveEma50: x1 * 0.01,
					solEma50vs200: x2 * 0.01,
					btcEma50vs200: x1 * 0.01);

				rows.Add (new LabeledCausalRow (causal, trueLabel, factMicroUp, factMicroDown));
				}

			return rows;
			}

		private static List<LabeledCausalRow> CloneRowsWithShuffledMoveLabel ( List<LabeledCausalRow> source, int seed )
			{
			var rng = new Random (seed);

			var moveFlags = source
				.Select (r => r.TrueLabel != 1)
				.ToList ();

			for (int i = moveFlags.Count - 1; i > 0; i--)
				{
				int j = rng.Next (i + 1);
				(moveFlags[i], moveFlags[j]) = (moveFlags[j], moveFlags[i]);
				}

			var result = new List<LabeledCausalRow> (source.Count);

			for (int i = 0; i < source.Count; i++)
				{
				var src = source[i];

				bool move = moveFlags[i];
				int newLabel = move ? 2 : 1;

				result.Add (new LabeledCausalRow (
					causal: src.Causal,
					trueLabel: newLabel,
					factMicroUp: false,
					factMicroDown: false));
				}

			return result;
			}
		}
	}
