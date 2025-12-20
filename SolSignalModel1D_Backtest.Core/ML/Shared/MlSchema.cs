using System;
using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Core.ML.Shared
	{
	/// <summary>
	/// Единый контракт ML-схемы: длина вектора фич.
	///
	/// Инвариант:
	/// - длина фич фиксирована для конкретной модели/датасета;
	/// - изменение длины = новая версия модели (старые .zip несовместимы).
	///
	/// Fail-fast:
	/// - при рассинхроне с CausalDataRow.FeatureNames падаем сразу при первом доступе к MlSchema.
	/// </summary>
	public static class MlSchema
		{
		/// <summary>
		/// Должно совпадать с CausalDataRow.FeatureNames.Count.
		/// Сейчас canonical causal-вектор = 22 фичи (см. CausalDataRow.BuildFeatureVector()).
		/// </summary>
		public const int FeatureCount = 22;

		static MlSchema ()
			{
			int causalCount = CausalDataRow.FeatureNames.Count;

			if (FeatureCount != causalCount)
				{
				throw new InvalidOperationException (
					$"[MlSchema] FeatureCount mismatch: MlSchema.FeatureCount={FeatureCount}, " +
					$"CausalDataRow.FeatureNames.Count={causalCount}. " +
					"Нужно синхронизировать схему: фиксированная длина вектора обязательна для ML.NET.");
				}
			}
		}
	}
