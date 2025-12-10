using System;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
	{
	/// <summary>
	/// Частичный класс PnlCalculator: решение, применять ли Anti-direction overlay.
	/// </summary>
	public static partial class PnlCalculator
		{
		/// <summary>
		/// Решает, применять ли Anti-D для данного дня и плеча.
		/// Использует только поля BacktestRecord, доступные после формирования
		/// causal-прогноза и forward-исходов.
		/// </summary>
		private static bool ShouldApplyAntiDirection ( BacktestRecord rec, double leverage )
			{
			if (rec == null)
				throw new ArgumentNullException (nameof (rec));

			// 1) Должен быть направленный сигнал:
			//    либо дневной up/down (PredLabel ∈ {0,2}),
			//    либо дневной flat с микро-сигналом.
			bool hasDirectionalSignal =
				rec.PredLabel == 2 ||
				rec.PredLabel == 0 ||
				(rec.PredLabel == 1 && (rec.PredMicroUp || rec.PredMicroDown));

			if (!hasDirectionalSignal)
				return false;

			// 2) Anti-D только если SL-слой ожидает первым именно SL
			//    в исходном направлении.
			if (!rec.SlHighDecision)
				return false;

			// 3) Оценка дневной волатильности MinMove должна быть положительной.
			double volProxy = rec.MinMove;

			if (double.IsNaN (volProxy) || volProxy <= 0.0)
				throw new InvalidOperationException ("[pnl] Forward.MinMove должен быть > 0 для Anti-D.");

			// Слишком «тухлые» (<0.5%) или экстремальные (>12%) дни не берём.
			if (volProxy < 0.005 || volProxy > 0.12)
				return false;

			// 4) Distance-to-liq по теоретической ликвидации должна быть
			//    существенно выше дневного хода.
			double liqAdversePct = ComputeLiqAdversePct (leverage);
			if (liqAdversePct <= 0.0)
				throw new InvalidOperationException ("[pnl] теоретическое расстояние до ликвидации должно быть > 0.");

			const double K = 2.0; // запас по дневным ходам до ликвидации
			if (liqAdversePct < K * volProxy)
				return false;

			return true;
			}
		}
	}
