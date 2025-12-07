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
		///
		/// ВАЖНО:
		/// - Anti-D допускается как для "чистых" up/down-дней (PredLabel ∈ {0,2}),
		///   так и для flat-дней, где микро-слой дал направление (PredMicroUp/PredMicroDown).
		/// - Если ни дневная модель, ни микро-слой не дали направленного сигнала,
		///   Anti-D не применяется.
		/// </summary>
		private static bool ShouldApplyAntiDirection ( PredictionRecord rec, double leverage )
			{
			if (rec == null)
				throw new ArgumentNullException (nameof (rec));

			// 1) Должен быть осмысленный направленный сигнал:
			//    либо дневной up/down (PredLabel ∈ {0,2}),
			//    либо дневной flat, но микро-слой дал направление.
			//    Это важно, чтобы не пытаться переворачивать дни без сигнала вообще.
			bool hasDirectionalSignal =
				rec.PredLabel == 2 ||
				rec.PredLabel == 0 ||
				(rec.PredLabel == 1 && (rec.PredMicroUp || rec.PredMicroDown));

			if (!hasDirectionalSignal)
				return false;

			// 2) Anti-D только если SL-модель ожидает первым именно SL в исходном направлении.
			//    Здесь используется бинарное решение SL-слоя (SlHighDecision),
			//    которое уже учитывает goLong/goShort при построении фичей.
			if (!rec.SlHighDecision)
				return false;

			// 3) Грубая оценка дневной волатильности (proxy) через MinMove.
			//    MinMove должен быть > 0 и не NaN, иначе это ошибка данных.
			double volProxy = rec.MinMove;

			if (double.IsNaN (volProxy) || volProxy <= 0.0)
				throw new InvalidOperationException ("[pnl] PredictionRecord.MinMove must be positive for Anti-D decision.");

			// Слишком тухлые (<0.5%) или слишком экстремальные (>12%) дни отбрасываем.
			// Это фильтр, чтобы Anti-D не включался на заведомо "неадекватных" режимах.
			if (volProxy < 0.005 || volProxy > 0.12)
				return false;

			// 4) Distance-to-liq ≥ K × volProxy.
			//    Здесь берётся теоретическая distance-to-liq (без backtest-мультипликатора),
			//    чтобы привязать решение к реальной биржевой ликвидации, а не к настройкам бэктеста.
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
