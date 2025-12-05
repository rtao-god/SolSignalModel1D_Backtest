namespace SolSignalModel1D_Backtest.Core.Data.DataBuilder.Diagnostics
	{
	/// <summary>
	/// Набор флажков для намеренных утечек в RowBuilder.
	/// Используются только в диагностике/self-check'ах.
	/// В прод-режиме все флаги должны быть false.
	/// </summary>
	public static class RowBuilderLeakageFlags
		{
		/// <summary>
		/// Хак через SolFwd1 (оставляем как есть, если уже был).
		/// </summary>
		public const bool EnableRowBuilderLeakSolFwd1 = false;

		/// <summary>
		/// Минутный peek: подмешивать цену первой 1m-свечи ПОСЛЕ baseline-exit
		/// в один из признаков фичей. В нормальном режиме должен быть false.
		/// </summary>
		public const bool EnableRowBuilderLeakSingleMinutePeek = false;

		/// <summary>
		/// Включает sanity-проверки с альтернативной 24h-разметкой (старый таргет).
		/// В проде должен оставаться false.
		/// </summary>
		public const bool EnableLegacy24hTargetSanityCheck = true;
		}
	}
