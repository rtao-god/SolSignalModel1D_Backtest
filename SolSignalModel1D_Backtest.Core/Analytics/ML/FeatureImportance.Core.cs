using Microsoft.ML;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.ML.Shared;

namespace SolSignalModel1D_Backtest.Core.Analytics.ML
	{
	/// <summary>
	/// Ядро PFI:
	/// - прогоняет датасет через модель;
	/// - считает базовый AUC;
	/// - делает permutation feature importance по AUC;
	/// - считает direction-метрики (mean pos/neg, corr).
	/// Не печатает в консоль и не хранит состояние.
	/// </summary>
	internal static class FeatureImportanceCore
		{
		/// <summary>
		/// Внутренний DTO для чтения данных из IDataView после Transform().
		/// </summary>
		private sealed class BinaryScoredRow
			{
			public bool Label { get; set; }

			[VectorType (MlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

			/// <summary>
			/// Сырой скор модели (логит / margin).
			/// Нужен для корреляции "фича ↔ скор".
			/// </summary>
			public float Score { get; set; }
			}

		/// <summary>
		/// Семпл для PFI (Label + Features) без Score.
		/// Выделен отдельно, чтобы не таскать лишнее в цикле permutation.
		/// </summary>
		private sealed class EvalSample
			{
			public bool Label { get; set; }

			[VectorType (MlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[MlSchema.FeatureCount];
			}

		/// <summary>
		/// Основной метод ядра PFI.
		/// Возвращает список FeatureStats + out-параметр baselineAuc.
		/// Никаких snapshot'ов и вывода — только вычисления.
		/// </summary>
		internal static List<FeatureStats> AnalyzeBinaryFeatureImportance (
			MLContext ml,
			ITransformer model,
			IDataView data,
			string[] featureNames,
			string tag,
			out double baselineAuc,
			string labelColumnName,
			string featuresColumnName )
			{
			if (ml == null) throw new ArgumentNullException (nameof (ml));
			if (model == null) throw new ArgumentNullException (nameof (model));
			if (data == null) throw new ArgumentNullException (nameof (data));
			if (featureNames == null) throw new ArgumentNullException (nameof (featureNames));
			if (featureNames.Length < MlSchema.FeatureCount)
				throw new ArgumentException ($"featureNames.Length ({featureNames.Length}) < MlSchema.FeatureCount ({MlSchema.FeatureCount})");

			// 1) Прогоняем data через модель, чтобы получить Score.
			var scoredBase = model.Transform (data);

			// 2) Базовый ROC-AUC по Score.
			var baseMetrics = ml.BinaryClassification.Evaluate (
				scoredBase,
				labelColumnName: labelColumnName,
				scoreColumnName: nameof (BinaryScoredRow.Score));

			baselineAuc = baseMetrics.AreaUnderRocCurve;

			// 3) Материализуем скоренные строки для direction-метрик и PFI.
			var scoredRows = ml.Data.CreateEnumerable<BinaryScoredRow> (
					scoredBase,
					reuseRowObject: false)
				.ToList ();

			if (scoredRows.Count == 0)
				return new List<FeatureStats> ();

			// Для PFI берём только Label + Features.
			var evalSamples = scoredRows
				.Select (r => new EvalSample
					{
					Label = r.Forward.TrueLabel,
					Features = (float[]) r.Causal.Features.Clone ()
					})
				.ToList ();

			// 4) PFI по AUC: для каждой фичи перемешиваем столбец и меряем падение AUC.
			int featCount = MlSchema.FeatureCount;
			var deltaAuc = new double[featCount];
			var rng = new Random (42); // фиксированный seed для воспроизводимости

			for (int j = 0; j < featCount; j++)
				{
				// Собираем столбец j.
				var col = new float[evalSamples.Count];
				for (int i = 0; i < evalSamples.Count; i++)
					col[i] = evalSamples[i].Features[j];

				// Если колонка константная — перемешивание ничего не меняет.
				bool nonConstant = false;
				for (int i = 1; i < col.Length; i++)
					{
					if (col[i] != col[0])
						{
						nonConstant = true;
						break;
						}
					}

				if (!nonConstant)
					{
					deltaAuc[j] = 0.0;
					continue;
					}

				// Перемешиваем индексы (Fisher–Yates).
				var idx = Enumerable.Range (0, col.Length).ToArray ();
				ShuffleInPlace (rng, idx);

				// Собираем пермутированные семплы (меняем только j-ю фичу).
				var permSamples = new List<EvalSample> (evalSamples.Count);
				for (int i = 0; i < evalSamples.Count; i++)
					{
					var src = evalSamples[i];
					var featCopy = (float[]) src.Causal.Features.Clone ();
					featCopy[j] = col[idx[i]];

					permSamples.Add (new EvalSample
						{
						Label = src.Forward.TrueLabel,
						Features = featCopy
						});
					}

				var permData = ml.Data.LoadFromEnumerable (permSamples);
				var scoredPerm = model.Transform (permData);

				var permMetrics = ml.BinaryClassification.Evaluate (
					scoredPerm,
					labelColumnName: labelColumnName,
					scoreColumnName: nameof (BinaryScoredRow.Score));

				double aucPerm = permMetrics.AreaUnderRocCurve;
				deltaAuc[j] = baselineAuc - aucPerm;
				}

			// 5) Direction-метрики (mean pos/neg, корреляции).
			var stats = ComputeFeatureStats (scoredRows, deltaAuc, featureNames);
			return stats;
			}

		/// <summary>
		/// Direction-метрики:
		/// - средние по классам;
		/// - корреляция с Label и Score;
		/// - support по классам.
		/// На входе уже есть deltaAuc по каждой фиче.
		/// </summary>
		private static List<FeatureStats> ComputeFeatureStats (
			List<BinaryScoredRow> rows,
			double[] deltaAuc,
			string[] featureNames )
			{
			int featCount = MlSchema.FeatureCount;

			var sumPos = new double[featCount];
			var sumNeg = new double[featCount];
			var cntPos = new int[featCount];
			var cntNeg = new int[featCount];

			var sumX = new double[featCount];
			var sumX2 = new double[featCount];

			var sumYLabel = new double[featCount];
			var sumYLabel2 = new double[featCount];
			var sumXYLabel = new double[featCount];

			var sumYScore = new double[featCount];
			var sumYScore2 = new double[featCount];
			var sumXYScore = new double[featCount];

			foreach (var r in rows)
				{
				double yLabel = r.Forward.TrueLabel ? 1.0 : 0.0;
				double yScore = r.Score;

				var f = r.Causal.Features;
				if (f == null) continue;

				int len = Math.Min (f.Length, featCount);

				for (int j = 0; j < len; j++)
					{
					double x = f[j];

					sumX[j] += x;
					sumX2[j] += x * x;

					if (r.Forward.TrueLabel)
						{
						sumPos[j] += x;
						cntPos[j]++;
						}
					else
						{
						sumNeg[j] += x;
						cntNeg[j]++;
						}

					sumYLabel[j] += yLabel;
					sumYLabel2[j] += yLabel * yLabel;
					sumXYLabel[j] += x * yLabel;

					sumYScore[j] += yScore;
					sumYScore2[j] += yScore * yScore;
					sumXYScore[j] += x * yScore;
					}
				}

			var res = new List<FeatureStats> (featCount);

			for (int j = 0; j < featCount; j++)
				{
				int pos = cntPos[j];
				int neg = cntNeg[j];
				int total = pos + neg;

				double meanPos = pos > 0 ? sumPos[j] / pos : double.NaN;
				double meanNeg = neg > 0 ? sumNeg[j] / neg : double.NaN;

				double corrLabel = 0.0;
				double corrScore = 0.0;

				if (total > 1)
					{
					double meanX = sumX[j] / total;

					// корреляция с Label ∈ {0,1}
					double meanYLabel = sumYLabel[j] / total;
					double covXYLabel = (sumXYLabel[j] / total) - meanX * meanYLabel;
					double varX = (sumX2[j] / total) - meanX * meanX;
					double varYLabel = (sumYLabel2[j] / total) - meanYLabel * meanYLabel;

					if (varX > 0 && varYLabel > 0)
						corrLabel = covXYLabel / Math.Sqrt (varX * varYLabel);

					// корреляция со Score
					double meanYScore = sumYScore[j] / total;
					double covXYScore = (sumXYScore[j] / total) - meanX * meanYScore;
					double varYScore = (sumYScore2[j] / total) - meanYScore * meanYScore;

					if (varX > 0 && varYScore > 0)
						corrScore = covXYScore / Math.Sqrt (varX * varYScore);
					}

				double dAuc = (j < deltaAuc.Length) ? deltaAuc[j] : 0.0;
				double importance = Math.Abs (dAuc);

				res.Add (new FeatureStats
					{
					Index = j,
					Name = j < featureNames.Length ? featureNames[j] : $"feat_{j}",
					ImportanceAuc = importance,
					DeltaAuc = dAuc,
					MeanPos = meanPos,
					MeanNeg = meanNeg,
					CorrLabel = corrLabel,
					CorrScore = corrScore,
					CountPos = pos,
					CountNeg = neg
					});
				}

			// сортируем по важности
			res.Sort (( a, b ) => b.ImportanceAuc.CompareTo (a.ImportanceAuc));
			return res;
			}

		/// <summary>
		/// Усечение имён фич, чтобы таблицы не разъезжались.
		/// Вынесено сюда, чтобы переиспользовать из принтера/summary.
		/// </summary>
		internal static string TruncateName ( string name, int maxLen )
			{
			if (string.IsNullOrEmpty (name) || name.Length <= maxLen)
				return name;

			return name.Substring (0, maxLen - 1) + "…";
			}

		/// <summary>
		/// Fisher–Yates shuffle для массива индексов.
		/// Используется при permute столбца в PFI.
		/// </summary>
		internal static void ShuffleInPlace ( Random rng, int[] idx )
			{
			for (int i = idx.Length - 1; i > 0; i--)
				{
				int j = rng.Next (i + 1);
				(idx[i], idx[j]) = (idx[j], idx[i]);
				}
			}
		}
	}
