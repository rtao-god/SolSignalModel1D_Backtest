using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace SolSignalModel1D_Backtest.Core.Analytics.ML
{
    /// <summary>
    /// Ядро PFI:
    /// - прогоняет датасет через модель;
    /// - считает базовый AUC;
    /// - делает permutation feature importance по AUC;
    /// - считает direction-метрики (mean pos/neg, corr).
    ///
    /// Контракт:
    /// - длина вектора фич фиксирована внутри одного вызова и должна совпадать с featureNames.Length;
    /// - любые рассинхроны схемы (Label/Features/Score) считаются ошибкой пайплайна и приводят к fail-fast.
    /// </summary>
    internal static class FeatureImportanceCore
    {
        /// <summary>
        /// DTO для чтения данных из IDataView после Transform().
        /// Привязка к колонкам делается через SchemaDefinition, чтобы поддерживать кастомные имена колонок.
        /// </summary>
        private sealed class BinaryScoredRow
        {
            public bool Label { get; set; }

            /// <summary>
            /// Вектор фич произвольной фиксированной длины (определяется схемой IDataView).
            /// </summary>
            public VBuffer<float> Features { get; set; }

            /// <summary>
            /// Сырой скор модели (логит / margin).
            /// Колонка должна существовать в результате model.Transform(data).
            /// </summary>
            public float Score { get; set; }
        }

        /// <summary>
        /// Семпл для permutation: Label + Features.
        /// Для LoadFromEnumerable длина вектора фиксируется первой строкой и должна быть одинаковой для всех.
        /// </summary>
        private sealed class EvalSample
        {
            public bool Label { get; set; }
            public float[] Features { get; set; } = Array.Empty<float>();
        }

        internal static List<FeatureStats> AnalyzeBinaryFeatureImportance(
            MLContext ml,
            ITransformer model,
            IDataView data,
            string[] featureNames,
            string tag,
            out double baselineAuc,
            string labelColumnName,
            string featuresColumnName)
        {
            if (ml == null) throw new ArgumentNullException(nameof(ml));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (featureNames == null) throw new ArgumentNullException(nameof(featureNames));
            if (featureNames.Length <= 0)
                throw new ArgumentException("featureNames must not be empty.", nameof(featureNames));
            if (string.IsNullOrWhiteSpace(labelColumnName))
                throw new ArgumentException("labelColumnName must not be empty.", nameof(labelColumnName));
            if (string.IsNullOrWhiteSpace(featuresColumnName))
                throw new ArgumentException("featuresColumnName must not be empty.", nameof(featuresColumnName));

            int featCount = featureNames.Length;

            // 1) Прогоняем data через модель, чтобы получить Score.
            var scoredBase = model.Transform(data);

            // 2) Базовый ROC-AUC по Score.
            var baseMetrics = ml.BinaryClassification.Evaluate(
                scoredBase,
                labelColumnName: labelColumnName,
                scoreColumnName: nameof(BinaryScoredRow.Score));

            baselineAuc = baseMetrics.AreaUnderRocCurve;

            // 3) Материализуем скоренные строки.
            // Привязываем свойства к фактическим именам колонок.
            var scoredSchema = SchemaDefinition.Create(typeof(BinaryScoredRow));
            scoredSchema[nameof(BinaryScoredRow.Label)].ColumnName = labelColumnName;
            scoredSchema[nameof(BinaryScoredRow.Features)].ColumnName = featuresColumnName;
            // Score ожидается как "Score" (стандарт ML.NET для binary trainers).
            scoredSchema[nameof(BinaryScoredRow.Score)].ColumnName = nameof(BinaryScoredRow.Score);

            var scoredRows = ml.Data.CreateEnumerable<BinaryScoredRow>(
                    scoredBase,
                    reuseRowObject: false,
                    schemaDefinition: scoredSchema)
                .ToList();

            if (scoredRows.Count == 0)
                return new List<FeatureStats>();

            // 3.1) Валидируем длину фич на первом же проходе.
            for (int i = 0; i < scoredRows.Count; i++)
            {
                var vb = scoredRows[i].Features;
                if (vb.Length != featCount)
                {
                    throw new InvalidOperationException(
                        $"[pfi-core:{tag}] Features length mismatch at row#{i}: " +
                        $"dataFeaturesLen={vb.Length}, featureNamesLen={featCount}. " +
                        $"labelColumn='{labelColumnName}', featuresColumn='{featuresColumnName}'.");
                }
            }

            // Для PFI берём только Label + Features (в массив).
            var evalSamples = new List<EvalSample>(scoredRows.Count);
            for (int i = 0; i < scoredRows.Count; i++)
            {
                var vb = scoredRows[i].Features;
                var feats = ToDenseArray(in vb, featCount);

                evalSamples.Add(new EvalSample
                {
                    Label = scoredRows[i].Label,
                    Features = feats
                });
            }

            // 4) PFI по AUC: для каждой фичи перемешиваем столбец и меряем падение AUC.
            var deltaAuc = new double[featCount];
            var rng = new Random(42); // фиксированный seed для воспроизводимости

            // Schema для permData: поддерживаем кастомные имена Label/Features.
            var permSchema = SchemaDefinition.Create(typeof(EvalSample));
            permSchema[nameof(EvalSample.Label)].ColumnName = labelColumnName;
            permSchema[nameof(EvalSample.Features)].ColumnName = featuresColumnName;

            // Критично: фиксируем размер вектора, чтобы совпал с моделью (Vector<Single, featCount>).
            permSchema[nameof(EvalSample.Features)].ColumnType =
                new VectorDataViewType(NumberDataViewType.Single, featCount);

            for (int j = 0; j < featCount; j++)
            {
                // Собираем столбец j.
                var col = new float[evalSamples.Count];
                for (int i = 0; i < evalSamples.Count; i++)
                    col[i] = evalSamples[i].Features[j];

                // Константный столбец: permutation не меняет AUC.
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
                var idx = Enumerable.Range(0, col.Length).ToArray();
                ShuffleInPlace(rng, idx);

                // Собираем пермутированные семплы (меняем только j-ю фичу).
                var permSamples = new List<EvalSample>(evalSamples.Count);
                for (int i = 0; i < evalSamples.Count; i++)
                {
                    var src = evalSamples[i];

                    var featCopy = (float[])src.Features.Clone();
                    featCopy[j] = col[idx[i]];

                    permSamples.Add(new EvalSample
                    {
                        Label = src.Label,
                        Features = featCopy
                    });
                }

                var permData = ml.Data.LoadFromEnumerable(permSamples, permSchema);
                var scoredPerm = model.Transform(permData);

                var permMetrics = ml.BinaryClassification.Evaluate(
                    scoredPerm,
                    labelColumnName: labelColumnName,
                    scoreColumnName: nameof(BinaryScoredRow.Score));

                double aucPerm = permMetrics.AreaUnderRocCurve;
                deltaAuc[j] = baselineAuc - aucPerm;
            }

            // 5) Direction-метрики.
            var stats = ComputeFeatureStats(scoredRows, deltaAuc, featureNames, featCount, tag);
            return stats;
        }

        private static List<FeatureStats> ComputeFeatureStats(
            List<BinaryScoredRow> rows,
            double[] deltaAuc,
            string[] featureNames,
            int featCount,
            string tag)
        {
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

            float[]? scratchDense = null;

            foreach (var r in rows)
            {
                double yLabel = r.Label ? 1.0 : 0.0;
                double yScore = r.Score;

                var vb = r.Features;
                if (vb.Length != featCount)
                {
                    throw new InvalidOperationException(
                        $"[pfi-core:{tag}] Features length mismatch in scored rows: " +
                        $"vb.Length={vb.Length}, expected={featCount}.");
                }

                ReadOnlySpan<float> fSpan;

                if (vb.IsDense)
                {
                    fSpan = vb.GetValues();
                    if (fSpan.Length != featCount)
                    {
                        throw new InvalidOperationException(
                            $"[pfi-core:{tag}] Dense Features values length mismatch: valuesLen={fSpan.Length}, expected={featCount}.");
                    }
                }
                else
                {
                    scratchDense ??= new float[featCount];
                    Array.Clear(scratchDense, 0, featCount);

                    var vals = vb.GetValues();
                    var idx = vb.GetIndices();

                    for (int k = 0; k < vals.Length; k++)
                    {
                        int j = idx[k];
                        if ((uint)j >= (uint)featCount)
                            throw new InvalidOperationException($"[pfi-core:{tag}] Sparse index out of range: j={j}, featCount={featCount}.");

                        scratchDense[j] = vals[k];
                    }

                    fSpan = scratchDense;
                }

                for (int j = 0; j < featCount; j++)
                {
                    double x = fSpan[j];

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

            var res = new List<FeatureStats>(featCount);

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

                    double meanYLabel = sumYLabel[j] / total;
                    double covXYLabel = (sumXYLabel[j] / total) - meanX * meanYLabel;
                    double varX = (sumX2[j] / total) - meanX * meanX;
                    double varYLabel = (sumYLabel2[j] / total) - meanYLabel * meanYLabel;

                    if (varX > 0 && varYLabel > 0)
                        corrLabel = covXYLabel / Math.Sqrt(varX * varYLabel);

                    double meanYScore = sumYScore[j] / total;
                    double covXYScore = (sumXYScore[j] / total) - meanX * meanYScore;
                    double varYScore = (sumYScore2[j] / total) - meanYScore * meanYScore;

                    if (varX > 0 && varYScore > 0)
                        corrScore = covXYScore / Math.Sqrt(varX * varYScore);
                }

                double dAuc = (j < deltaAuc.Length) ? deltaAuc[j] : 0.0;

                res.Add(new FeatureStats
                {
                    Index = j,
                    Name = j < featureNames.Length ? featureNames[j] : $"feat_{j}",
                    ImportanceAuc = Math.Abs(dAuc),
                    DeltaAuc = dAuc,
                    MeanPos = meanPos,
                    MeanNeg = meanNeg,
                    CorrLabel = corrLabel,
                    CorrScore = corrScore,
                    CountPos = pos,
                    CountNeg = neg
                });
            }

            res.Sort((a, b) => b.ImportanceAuc.CompareTo(a.ImportanceAuc));
            return res;
        }

        private static float[] ToDenseArray(in VBuffer<float> vb, int expectedLen)
        {
            if (vb.Length != expectedLen)
            {
                throw new InvalidOperationException(
                    $"[pfi-core] Features length mismatch while densifying: vb.Length={vb.Length}, expected={expectedLen}.");
            }

            var dst = new float[expectedLen];

            if (vb.IsDense)
            {
                vb.GetValues().CopyTo(dst);
                return dst;
            }

            var vals = vb.GetValues();
            var idx = vb.GetIndices();

            for (int k = 0; k < vals.Length; k++)
            {
                int j = idx[k];
                if ((uint)j >= (uint)expectedLen)
                    throw new InvalidOperationException($"[pfi-core] Sparse index out of range: j={j}, expectedLen={expectedLen}.");

                dst[j] = vals[k];
            }

            return dst;
        }

        internal static string TruncateName(string name, int maxLen)
        {
            if (string.IsNullOrEmpty(name) || name.Length <= maxLen)
                return name;

            return name.Substring(0, maxLen - 1) + "…";
        }

        internal static void ShuffleInPlace(Random rng, int[] idx)
        {
            for (int i = idx.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (idx[i], idx[j]) = (idx[j], idx[i]);
            }
        }
    }
}
