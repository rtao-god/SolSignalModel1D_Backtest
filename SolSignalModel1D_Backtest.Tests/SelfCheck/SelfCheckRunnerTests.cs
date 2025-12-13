using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.SanityChecks;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks;
using SolSignalModel1D_Backtest.Tests.TestUtils;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.SelfCheck
	{
	public class SelfCheckRunnerTests
		{
		private static BacktestRecord MakeRecord ( DateTime dateUtc, int trueLabel, int predLabel )
			{
			static (double Up, double Flat, double Down) MakeTriProbs ( int cls )
				{
				const double Hi = 0.90;
				const double Lo = 0.05;

				return cls switch
					{
						2 => (Hi, Lo, Lo),
						1 => (Lo, Hi, Lo),
						0 => (Lo, Lo, Hi),
						_ => throw new ArgumentOutOfRangeException (nameof (cls), cls, "PredLabel must be in [0..2].")
						};
				}

			var (pUp, pFlat, pDown) = MakeTriProbs (predLabel);

			return new BacktestRecord
				{
				Causal = new CausalPredictionRecord
					{
					DateUtc = dateUtc,
					TrueLabel = trueLabel,
					PredLabel = predLabel,
					PredLabel_Day = predLabel,
					PredLabel_DayMicro = predLabel,

					ProbUp_Day = pUp,
					ProbFlat_Day = pFlat,
					ProbDown_Day = pDown,

					ProbUp_DayMicro = pUp,
					ProbFlat_DayMicro = pFlat,
					ProbDown_DayMicro = pDown,

					ProbUp_Total = pUp,
					ProbFlat_Total = pFlat,
					ProbDown_Total = pDown,

					Conf_Day = Math.Max (pUp, Math.Max (pFlat, pDown))
					},

				Forward = new ForwardOutcomes
					{
					DateUtc = dateUtc,
					WindowEndUtc = dateUtc.AddHours (24),

					Entry = 100.0,
					MaxHigh24 = 110.0,
					MinLow24 = 90.0,
					Close24 = 100.0,
					MinMove = 0.01,
					DayMinutes = Array.Empty<Candle1m> ()
					}
				};
			}

		[Fact]
		public async Task DailyCheck_FlagsTooGoodTrainAccuracy ()
			{
			var datesUtc = NyTestDates.BuildNyWeekdaySeriesUtc (
				startNyLocalDate: NyTestDates.NyLocal (2020, 1, 1, 0),
				count: 300,
				hour: 8);

			var records = new List<BacktestRecord> (datesUtc.Count);

			for (int i = 0; i < datesUtc.Count; i++)
				{
				int label = i % 3;
				records.Add (MakeRecord (datesUtc[i], label, label));
				}

			var ctx = new SelfCheckContext
				{
				Records = records,
				TrainUntilUtc = new DateTime (2035, 1, 1, 0, 0, 0, DateTimeKind.Utc) // всё в train
				};

			var result = await SelfCheckRunner.RunAsync (ctx);

			Assert.False (result.Success);
			Assert.Contains (result.Errors, e => e.Contains ("train accuracy", StringComparison.OrdinalIgnoreCase));
			}

		[Fact]
		public async Task DailyCheck_AllowsReasonableAccuracy ()
			{
			var datesUtc = NyTestDates.BuildNyWeekdaySeriesUtc (
				startNyLocalDate: NyTestDates.NyLocal (2020, 1, 1, 0),
				count: 300,
				hour: 8);

			var records = new List<BacktestRecord> (datesUtc.Count);

			for (int i = 0; i < datesUtc.Count; i++)
				{
				int trueLabel = i % 3;
				int predLabel = (i % 10 < 6) ? trueLabel : (trueLabel + 1) % 3;
				records.Add (MakeRecord (datesUtc[i], trueLabel, predLabel));
				}

			var ctx = new SelfCheckContext
				{
				Records = records,
				TrainUntilUtc = datesUtc[200] // часть уйдёт в OOS (как именно — решает боевой split)
				};

			var result = await SelfCheckRunner.RunAsync (ctx);

			Assert.True (result.Success);
			}
		}
	}
