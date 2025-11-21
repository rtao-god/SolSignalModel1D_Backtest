using System;
using SolSignalModel1D_Backtest.Core.Data;

namespace SolSignalModel1D_Backtest.Core.Utils.Pnl
	{
	/// <summary>
	/// Частичный класс PnlCalculator: решение, применять ли Anti-D overlay для дня.
	/// </summary>
	public static partial class PnlCalculator
		{
		/// <summary>
		/// Решает, применять ли Anti-D для данного дня и плеча.
		/// Использует только данные SL-слоя и волатильности, доступные в PredictionRecord.
		/// Здесь используется теоретическая distance-to-liq (ComputeLiqAdversePct),
		/// чтобы логика Anti-D не зависела от параметров backtest-ликвидации.
		/// </summary>
		private static bool ShouldApplyAntiDirection ( PredictionRecord rec, double leverage )
			{
			if (rec == null)
				throw new ArgumentNullException (nameof (rec));

			// 1) Только non-flat дни (pred ∈ {0=DOWN, 2=UP}).
			if (rec.PredLabel != 0 && rec.PredLabel != 2)
				return false;

			// 2) Anti-D только если SL-модель ожидает первым именно SL в исходном направлении.
			if (!rec.SlHighDecision)
				return false;

			// 3) Грубая оценка дневной волатильности (proxy) через MinMove.
			double volProxy = rec.MinMove;

			if (double.IsNaN (volProxy) || volProxy <= 0.0)
				throw new InvalidOperationException ("[pnl] PredictionRecord.MinMove must be positive for Anti-D decision.");

			// Слишком тухлые или слишком экстремальные дни отбрасываем.
			if (volProxy < 0.005 || volProxy > 0.12)
				return false;

			// 4) Distance-to-liq ≥ K × volProxy.
			// Здесь берётся теоретическая distance-to-liq.
			double liqAdversePct = ComputeLiqAdversePct (leverage);
			if (liqAdversePct <= 0.0)
				throw new InvalidOperationException ("[pnl] theoretical liquidation adverse move must be positive for Anti-D.");

			const double K = 2.0; // запас по дневным ходам MinMove до ликвидации
			if (liqAdversePct < K * volProxy)
				return false;

			// 5) При необходимости сюда можно добавить дополнительные фильтры (mean-reversion, тренды и т.п.).
			return true;
			}
		}
	}
