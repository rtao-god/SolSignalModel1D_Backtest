using System;

namespace SolSignalModel1D_Backtest.Core.Analytics.ML
	{
	/// <summary>
	/// Агрегированные статистики по одной фиче для бинарной модели.
	/// Используется как DTO между ядром PFI, принтером и summary.
	/// </summary>
	public sealed class FeatureStats
		{
		/// <summary>Индекс признака (slot в векторе Features).</summary>
		public int Index { get; set; }

		/// <summary>Человекочитаемое имя признака.</summary>
		public string Name { get; set; } = string.Empty;

		/// <summary>
		/// Абсолютная важность по ROC-AUC:
		/// |AUC_baseline - AUC_permuted|.
		/// </summary>
		public double ImportanceAuc { get; set; }

		/// <summary>
		/// Подпись по AUC (AUC_baseline - AUC_permuted).
		/// Если &gt; 0 — при перемешивании метрика падает.
		/// </summary>
		public double DeltaAuc { get; set; }

		/// <summary>Среднее значение признака в положительном классе (Label = true).</summary>
		public double MeanPos { get; set; }

		/// <summary>Среднее значение признака в отрицательном классе (Label = false).</summary>
		public double MeanNeg { get; set; }

		/// <summary>Разница средних: MeanPos - MeanNeg.</summary>
		public double DeltaMean => MeanPos - MeanNeg;

		/// <summary>
		/// Корреляция Пирсона фичи с таргетом (Label ∈ {0,1}).
		/// </summary>
		public double CorrLabel { get; set; }

		/// <summary>
		/// Корреляция Пирсона фичи со скором модели (Score).
		/// </summary>
		public double CorrScore { get; set; }

		/// <summary>Количество примеров положительного класса.</summary>
		public int CountPos { get; set; }

		/// <summary>Количество примеров отрицательного класса.</summary>
		public int CountNeg { get; set; }
		}
	}
