using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;
using System;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl
	{
	public static partial class PnlCalculator
		{
		private static bool TryResolveDirection (
			BacktestRecord rec,
			PnlPredictionMode predictionMode,
			out bool goLong,
			out bool goShort )
			{
			goLong = false;
			goShort = false;

			switch (predictionMode)
				{
				case PnlPredictionMode.DayOnly:
						{
						goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
						goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);
						break;
						}
				case PnlPredictionMode.DayPlusMicro:
						{
						int cls = rec.PredLabel_DayMicro;
						goLong = cls == 2;
						goShort = cls == 0;
						break;
						}
				case PnlPredictionMode.DayPlusMicroPlusSl:
						{
						double up = rec.ProbUp_Total;
						double down = rec.ProbDown_Total;
						double flat = rec.ProbFlat_Total;

						goLong = up > down && up > flat;
						goShort = down > up && down > flat;
						break;
						}
				default:
					throw new ArgumentOutOfRangeException (nameof (predictionMode), predictionMode, "Unknown prediction mode");
				}

			return goLong || goShort;
			}

		private static bool ShouldApplyAntiDirection ( BacktestRecord rec, double leverage )
			{
			if (rec == null) throw new ArgumentNullException (nameof (rec));
			if (leverage <= 0.0) throw new InvalidOperationException ("[pnl] leverage must be > 0 in ShouldApplyAntiDirection().");

			// Anti-D только если SL-слой явно пометил день как рискованный.
			// null => SL слой не посчитан/не применим => Anti-D не используем.
			if (rec.SlHighDecision != true)
				return false;

			// MinMove — прокси дневного хода (должен быть каузальным, не из forward).
			double volProxy = rec.MinMove;
			if (double.IsNaN (volProxy) || volProxy <= 0.0)
				throw new InvalidOperationException ("[pnl] MinMove must be > 0 for Anti-D gating.");

			// Слишком «тухлые» или экстремальные дни не берём.
			if (volProxy < 0.005 || volProxy > 0.12)
				return false;

			// Дистанция до ликвидации должна иметь запас относительно дневного хода.
			double liqAdversePct = ComputeLiqAdversePct (leverage);

			const double K = 2.0; // запас по дневным ходам до ликвидации
			if (liqAdversePct < K * volProxy)
				return false;

			return true;
			}
		}
	}
