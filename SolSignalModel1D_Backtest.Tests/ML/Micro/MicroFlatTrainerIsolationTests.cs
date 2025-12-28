using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Tests.ML.Micro
	{
	public sealed class MicroFlatTrainerIsolationTests
		{
		[Fact]
		public void BuildMicroFlatModel_ReturnsNull_WhenNotEnoughMicroRows ()
			{
			var rows = BuildNyWeekdayRows (countTotal: 120, countMicro: 20);

			var ml = new MLContext (seed: 42);

			var model = MicroFlatTrainer.BuildMicroFlatModel (ml, rows);

			Assert.Null (model);
			}

		[Fact]
		public void BuildMicroFlatModel_Throws_OnSingleClassDataset ()
			{
			var rows = BuildNyWeekdayRows (countTotal: 220, countMicro: 80);

			// Превращаем все micro-дни в один класс (все up).
			var singleClass = rows
				.Select (r =>
				{
					if (!r.FactMicroUp && !r.FactMicroDown) return r;

					return new LabeledCausalRow (
						causal: r.Causal,
						trueLabel: r.TrueLabel,
						factMicroUp: true,
						factMicroDown: false);
				})
				.ToList ();

			var ml = new MLContext (seed: 42);

			Assert.Throws<InvalidOperationException> (() =>
				MicroFlatTrainer.BuildMicroFlatModel (ml, singleClass));
			}

		[Fact]
		public void BuildMicroFlatModel_Trains_WhenEnoughMicroRows ()
			{
			var rows = BuildNyWeekdayRows (countTotal: 260, countMicro: 120);

			var ml = new MLContext (seed: 42);

			var model = MicroFlatTrainer.BuildMicroFlatModel (ml, rows);

			Assert.NotNull (model);
			}

		private static List<LabeledCausalRow> BuildNyWeekdayRows ( int countTotal, int countMicro )
			{
			if (countTotal <= 0) throw new ArgumentOutOfRangeException (nameof (countTotal));
			if (countMicro < 0 || countMicro > countTotal) throw new ArgumentOutOfRangeException (nameof (countMicro));

			var nyTz = NyWindowing.NyTz;

			var res = new List<LabeledCausalRow> (countTotal);
			var dt = new DateTime (2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);

			int idx = 0;
			int microMade = 0;

			while (res.Count < countTotal)
				{
				var ny = TimeZoneInfo.ConvertTimeFromUtc (dt, nyTz);
				if (ny.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
					{
					dt = dt.AddDays (1);
					continue;
					}

				bool isMicro = microMade < countMicro;
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

