using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Micro
	{
	public sealed class MicroLeakageTests
		{
		[Fact]
		public void BuildMicroFlatModel_ReturnsNull_WhenTooFewMicroDays ()
			{
			var rows = BuildNyWeekdayRows (
				startUtc: new DateTime (2025, 1, 2, 12, 0, 0, DateTimeKind.Utc),
				totalDays: 120,
				microDays: 10);

			var ml = new MLContext (seed: 42);

			var model = MicroFlatTrainer.BuildMicroFlatModel (ml, rows);

			Assert.Null (model);
			}

		[Fact]
		public void BuildMicroFlatModel_ReturnsModel_WhenEnoughMicroDays ()
			{
			var rows = BuildNyWeekdayRows (
				startUtc: new DateTime (2025, 1, 2, 12, 0, 0, DateTimeKind.Utc),
				totalDays: 200,
				microDays: 60);

			var ml = new MLContext (seed: 42);

			var model = MicroFlatTrainer.BuildMicroFlatModel (ml, rows);

			Assert.NotNull (model);
			}

		private static List<LabeledCausalRow> BuildNyWeekdayRows ( DateTime startUtc, int totalDays, int microDays )
			{
			if (startUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("startUtc must be UTC.", nameof (startUtc));
			if (totalDays <= 0) throw new ArgumentOutOfRangeException (nameof (totalDays));
			if (microDays < 0 || microDays > totalDays) throw new ArgumentOutOfRangeException (nameof (microDays));

			var nyTz = NyWindowing.NyTz;

			var res = new List<LabeledCausalRow> (totalDays);

			var dt = startUtc;
			int idx = 0;
			int microMade = 0;

			while (res.Count < totalDays)
				{
				var ny = TimeZoneInfo.ConvertTimeFromUtc (dt, nyTz);
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					{
					dt = dt.AddDays (1);
					continue;
					}

				bool isMicro = microMade < microDays;
				bool microUp = isMicro && (microMade % 2 == 0);
				bool microDown = isMicro && !microUp;

				if (isMicro) microMade++;

				res.Add (MakeRow (dt, idx, isMicro, microUp, microDown));
				dt = dt.AddDays (1);
				idx++;
				}

			return res;
			}

		private static LabeledCausalRow MakeRow ( DateTime dateUtc, int idx, bool isMicro, bool microUp, bool microDown )
			{
			if (microUp && microDown)
				throw new InvalidOperationException ("microUp and microDown cannot be true одновременно.");

			double dir = microUp ? 2.0 : (microDown ? -2.0 : 0.0);

			var entryUtc = NyWindowing.CreateNyTradingEntryUtcOrThrow (new EntryUtc (dateUtc), NyWindowing.NyTz);

			var causal = new CausalDataRow (
				entryUtc: entryUtc,
				regimeDown: false,
				isMorning: true,
				hardRegime: 0,
				minMove: 0.03,

				solRet30: dir,
				btcRet30: 0.01 * (idx + 1),
				solBtcRet30: 0.001 * (idx + 1),

				solRet1: 0.002 * (idx + 1),
				solRet3: 0.003 * (idx + 1),
				btcRet1: 0.004 * (idx + 1),
				btcRet3: 0.005 * (idx + 1),

				fngNorm: 0.10,
				dxyChg30: -0.02,
				goldChg30: 0.01,

				btcVs200: 0.2,

				solRsiCenteredScaled: 0.3,
				rsiSlope3Scaled: 0.4,

				gapBtcSol1: 0.01,
				gapBtcSol3: 0.02,

				atrPct: 0.05,
				dynVol: 0.06,

				solAboveEma50: 1.0,
				solEma50vs200: 0.1,
				btcEma50vs200: 0.2);

			return new LabeledCausalRow (
				causal: causal,
				trueLabel: isMicro ? 1 : 2,
				factMicroUp: microUp,
				factMicroDown: microDown);
			}
		}
	}

