using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using System;
using System.Collections.Generic;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.ML.Daily
	{
	public sealed class DailyDatasetBuilderLeakageTests
		{
		[Fact]
		public void Build_CutsTrainRowsByBaselineExit_NotByEntryUtc ()
			{
			var nyTz = NyWindowing.NyTz;

			var entryUtc = NyTestDates.ToUtc (NyTestDates.NyLocal (2025, 1, 6, 8, 0));
			var exitUtc = NyWindowing.ComputeBaselineExitUtc (entryUtc, nyTz);
			Assert.True (exitUtc > entryUtc);

			var rows = new List<LabeledCausalRow>
				{
				CreateRow (dateUtc: entryUtc, label: 2, regimeDown: false)
				};

			var midTicks = entryUtc.Ticks + (exitUtc.Ticks - entryUtc.Ticks) / 2;
			var trainUntilBetween = new DateTime (midTicks, DateTimeKind.Utc);

			var dsBeforeExit = DailyDatasetBuilder.Build (rows, trainUntilBetween, false, false, 0.5, null);
			Assert.Empty (dsBeforeExit.TrainRows);

			var trainUntilAfterExit = exitUtc.AddMinutes (1);
			var dsAfterExit = DailyDatasetBuilder.Build (rows, trainUntilAfterExit, false, false, 0.5, null);

			Assert.Single (dsAfterExit.TrainRows);
			Assert.Equal (entryUtc, dsAfterExit.TrainRows[0].DateUtc);
			}

		[Fact]
		public void Build_AllTrainListsContainOnlyTrainEntries_ByTrainBoundary ()
			{
			var nyTz = NyWindowing.NyTz;

			var datesUtc = NyTestDates.BuildNyWeekdaySeriesUtc (
				startNyLocalDate: NyTestDates.NyLocal (2025, 1, 1, 0),
				count: 120,
				hour: 8);

			var rows = new List<LabeledCausalRow> (datesUtc.Count);
			for (int i = 0; i < datesUtc.Count; i++)
				{
				rows.Add (CreateRow (
					dateUtc: datesUtc[i],
					label: i % 3,
					regimeDown: (i % 5 == 0)));
				}

			var pivotEntry = datesUtc[^20];
			var pivotExit = NyWindowing.ComputeBaselineExitUtc (pivotEntry, nyTz);
			var trainUntilUtc = pivotExit.AddMinutes (1);

			var boundary = new TrainBoundary (trainUntilUtc, nyTz);

			var ds = DailyDatasetBuilder.Build (rows, trainUntilUtc, false, false, 0.5, null);

			Assert.NotEmpty (ds.TrainRows);

			static void AssertAllTrain ( IEnumerable<LabeledCausalRow> xs, TrainBoundary b, string tag )
				{
				foreach (var r in xs)
					{
					Assert.True (
						b.IsTrainEntry (r.DateUtc),
						$"{tag} contains non-train entry by TrainBoundary: {r.DateUtc:O}");
					}
				}

			AssertAllTrain (ds.TrainRows, boundary, nameof (ds.TrainRows));
			AssertAllTrain (ds.MoveTrainRows, boundary, nameof (ds.MoveTrainRows));
			AssertAllTrain (ds.DirNormalRows, boundary, nameof (ds.DirNormalRows));
			AssertAllTrain (ds.DirDownRows, boundary, nameof (ds.DirDownRows));
			}

		private static LabeledCausalRow CreateRow ( DateTime dateUtc, int label, bool regimeDown )
			{
			double solRet1 = label == 2 ? 0.02 : label == 0 ? -0.02 : 0.0;

			var causal = new CausalDataRow (
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

			return new LabeledCausalRow (
				causal: causal,
				trueLabel: label,
				factMicroUp: false,
				factMicroDown: false);
			}
		}
	}
