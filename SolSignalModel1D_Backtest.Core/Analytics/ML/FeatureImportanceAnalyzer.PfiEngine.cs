using Microsoft.ML;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.ML.Shared;

namespace SolSignalModel1D_Backtest.Core.Analytics.ML
	{
	/// <summary>
	/// PFI-движок и расчёт direction-метрик.
	/// </summary>
	public static partial class FeatureImportanceAnalyzer
		{
		/// <summary>
		/// Внутренний DTO: Label + Features + Score после прогонки через модель.
		/// Важно: не должен ссылаться на доменные сущности (BacktestRecord/Causal/Forward).
		/// </summary>
		private sealed class BinaryScoredRow
			{
			public bool Label { get; set; }

			[VectorType (MlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[MlSchema.FeatureCount];

			public float Score { get; set; }
			}

		/// <summary>
		/// Внутренний DTO для PFI: Label + Features без Score.
		/// </summary>
		private sealed class EvalSample
			{
			public bool Label { get; set; }

			[VectorType (MlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[MlSchema.FeatureCount];
			}

		/// <summary>
		/// Прогоняет data через модель, считает baseline AUC
		/// и материализует BinaryScoredRow (Label + Features + Score).
		/// </summary>
		private static (double baseAuc, List<BinaryScoredRow> scoredRows) ScoreAndGetBaseline (
			MLContext ml,
			ITransformer model,
			IDataView data,
			string tag,
			string labelColumnName )
			{
			var scoredBase = model.Transform (data);

			var baseMetrics = ml.BinaryClassification.Evaluate (
				scoredBase,
				labelColumnName: labelColumnName,
				scoreColumnName: nameof (BinaryScoredRow.Score)); // "Score"

			double baseAuc = baseMetrics.AreaUnderRocCurve;
			Console.WriteLine ($"[pfi:{tag}] baseline AUC = {baseAuc:F4}");

			var scoredRows = ml.Data.CreateEnumerable<BinaryScoredRow> (
					scoredBase,
					reuseRowObject: false)
				.ToList ();

			return (baseAuc, scoredRows);
			}

		/// <summary>
		/// Строит список EvalSample из скоренных строк:
		/// оставляем только Label + Features, чтобы не таскать Score в PFI.
		/// </summary>
		private static List<EvalSample> BuildEvalSamples ( List<BinaryScoredRow> scoredRows )
			{
			var evalSamples = new List<EvalSample> (scoredRows.Count);

			foreach (var r in scoredRows)
				{
				if (r.Features == null)
					{
					throw new InvalidOperationException (
						"[pfi] Features is null in scored rows. " +
						"Это признак несоответствия схемы IDataView (колонки Label/Features).");
					}

				var feats = (float[]) r.Features.Clone ();

				evalSamples.Add (new EvalSample
					{
					Label = r.Label,
					Features = feats
					});
				}

			return evalSamples;
			}

		/// <summary>
		/// PFI: для каждой фичи j случайно перемешивает столбец,
		/// прогоняет через модель и считает deltaAUC = baseAuc - aucPerm.
		/// </summary>
		private static double[] ComputePermutationDeltaAuc (
			MLContext ml,
			ITransformer model,
			List<EvalSample> evalSamples,
			double baseAuc,
			string labelColumnName )
			{
			int featCount = MlSchema.FeatureCount;
			var deltaAuc = new double[featCount];

			var rng = new Random (42); // фиксированный seed для воспроизводимости

			for (int j = 0; j < featCount; j++)
				{
				// Забираем целиком j-ю колонку.
				var col = new float[evalSamples.Count];
				for (int i = 0; i < evalSamples.Count; i++)
					col[i] = evalSamples[i].Features[j];

				// Проверяем, константная ли колонка.
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

				// Формируем пермутированный набор семплов.
				var permSamples = new List<EvalSample> (evalSamples.Count);
				for (int i = 0; i < evalSamples.Count; i++)
					{
					var src = evalSamples[i];

					// Клонирование обязательно: permutation не должен мутировать исходные данные.
					var featCopy = (float[]) src.Features.Clone ();
					featCopy[j] = col[idx[i]];

					permSamples.Add (new EvalSample
						{
						Label = src.Label,
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
				deltaAuc[j] = baseAuc - aucPerm;
				}

			return deltaAuc;
			}

		/// <summary>
		/// Вычисляет средние/корреляции по скоренным строкам и готовому deltaAuc.
		/// На выходе — список FeatureStats, отсортированный по важности.
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
				double yLabel = r.Label ? 1.0 : 0.0;
				double yScore = r.Score;

				var f = r.Features;
				if (f == null)
					{
					throw new InvalidOperationException (
						"[pfi] Features is null in scored rows. " +
						"Это означает несоответствие схемы IDataView ожидаемым колонкам (Label/Features).");
					}

				int len = Math.Min (f.Length, featCount);

				for (int j = 0; j < len; j++)
					{
					double x = f[j];

					sumX[j] += x;
					sumX2[j] += x * x;

					if (r.Label)
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

					// ---- корреляция с Label ∈ {0,1} ----
					double meanYLabel = sumYLabel[j] / total;
					double covXYLabel = (sumXYLabel[j] / total) - meanX * meanYLabel;
					double varX = (sumX2[j] / total) - meanX * meanX;
					double varYLabel = (sumYLabel2[j] / total) - meanYLabel * meanYLabel;

					if (varX > 0 && varYLabel > 0)
						corrLabel = covXYLabel / Math.Sqrt (varX * varYLabel);

					// ---- корреляция со скором Score ----
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

			res.Sort (( a, b ) => b.ImportanceAuc.CompareTo (a.ImportanceAuc));
			return res;
			}

		/// <summary>
		/// Fisher–Yates shuffle для int-массива.
		/// Используется в PFI для перемешивания столбца.
		/// </summary>
		private static void ShuffleInPlace ( Random rng, int[] idx )
			{
			for (int i = idx.Length - 1; i > 0; i--)
				{
				int j = rng.Next (i + 1);
				(idx[i], idx[j]) = (idx[j], idx[i]);
				}
			}
		}
	}
