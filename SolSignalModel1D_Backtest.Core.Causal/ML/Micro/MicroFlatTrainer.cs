using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Causal.ML.Utils;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Micro
{
    public static class MicroFlatTrainer
    {
        private const int MinMicroRowsForTraining = 40;

        public static ITransformer? BuildMicroFlatModel(MLContext ml, IReadOnlyList<LabeledCausalRow> rows)
        {
            if (ml == null) throw new ArgumentNullException(nameof(ml));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (rows.Count == 0)
                throw new ArgumentException("rows must be non-empty.", nameof(rows));

            // Инвариант: NyTradingEntryUtc.Value всегда UTC.
            for (int i = 0; i < rows.Count; i++)
            {
                var entryUtcDt = rows[i].EntryUtc.Value;
                if (entryUtcDt.Kind != DateTimeKind.Utc)
                {
                    throw new InvalidOperationException(
                        $"[2stage-micro] rows[{i}].EntryUtc must be UTC, got Kind={entryUtcDt.Kind}, Date={entryUtcDt:O}.");
                }
            }

            var trainUntilUtc = DeriveMaxBaselineExitUtc(rows, NyWindowing.NyTz);
            var trainUntilExitDayKeyUtc = TrainUntilExitDayKeyUtc.FromBaselineExitUtcOrThrow(trainUntilUtc);

            var dataset = MicroDatasetBuilder.Build(
                allRows: rows,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc);

            var flatsRaw = dataset.MicroRows;

            if (flatsRaw.Count == 0)
            {
                Console.WriteLine("[2stage-micro] нет ни одного размеченного микро-дня, микро-слой отключён.");
                return null;
            }

            if (flatsRaw.Count < MinMicroRowsForTraining)
            {
                Console.WriteLine(
                    $"[2stage-micro] датасет микро-дней слишком мал (flats={flatsRaw.Count}, " +
                    $"min={MinMicroRowsForTraining}), микро-слой отключён для этого прогона.");
                return null;
            }

            int upCount = 0, dnCount = 0;
            for (int i = 0; i < flatsRaw.Count; i++)
            {
                if (flatsRaw[i].FactMicroUp) upCount++;
                if (flatsRaw[i].FactMicroDown) dnCount++;
            }

            if (upCount == 0 || dnCount == 0)
            {
                throw new InvalidOperationException(
                    $"[2stage-micro] датасет микро-дней одноклассовый (up={upCount}, down={dnCount}) " +
                    $"при flats={flatsRaw.Count}. Проверь разметку FactMicroUp/FactMicroDown.");
            }

            int take = Math.Min(upCount, dnCount);

            var upBalanced = new List<LabeledCausalRow>(take);
            var dnBalanced = new List<LabeledCausalRow>(take);

            int upNeed = take;
            int dnNeed = take;

            for (int i = 0; i < flatsRaw.Count && (upNeed > 0 || dnNeed > 0); i++)
            {
                var r = flatsRaw[i];

                if (upNeed > 0 && r.FactMicroUp)
                {
                    upBalanced.Add(r);
                    upNeed--;
                    continue;
                }

                if (dnNeed > 0 && r.FactMicroDown)
                {
                    dnBalanced.Add(r);
                    dnNeed--;
                    continue;
                }
            }

            if (upBalanced.Count != take || dnBalanced.Count != take)
            {
                throw new InvalidOperationException(
                    $"[2stage-micro] failed to build balanced micro set: take={take}, up={upBalanced.Count}, down={dnBalanced.Count}.");
            }

            // Слияние двух отсортированных по времени списков без OrderBy.
            var flats = new List<LabeledCausalRow>(take * 2);
            int iu = 0, id = 0;

            while (iu < upBalanced.Count && id < dnBalanced.Count)
            {
                if (upBalanced[iu].EntryUtc.Value <= dnBalanced[id].EntryUtc.Value)
                    flats.Add(upBalanced[iu++]);
                else
                    flats.Add(dnBalanced[id++]);
            }

            while (iu < upBalanced.Count) flats.Add(upBalanced[iu++]);
            while (id < dnBalanced.Count) flats.Add(dnBalanced[id++]);

            var samples = new List<MlSampleBinary>(flats.Count);
            int? featureDim = null;
            bool hasNaN = false;
            bool hasInf = false;

            foreach (var r in flats)
            {
                var feats = MlTrainingUtils.ToFloatFixed(r.Causal.FeaturesVector);

                if (feats == null)
                    throw new InvalidOperationException("[2stage-micro] ToFloatFixed вернул null для вектора признаков микро-слоя.");

                if (featureDim == null)
                {
                    featureDim = feats.Length;
                    if (featureDim <= 0)
                        throw new InvalidOperationException("[2stage-micro] длина вектора признаков равна 0.");
                }
                else if (feats.Length != featureDim.Value)
                {
                    throw new InvalidOperationException(
                        $"[2stage-micro] неконсистентная длина признаков: ожидалось {featureDim.Value}, получено {feats.Length}.");
                }

                for (int i = 0; i < feats.Length; i++)
                {
                    if (float.IsNaN(feats[i])) hasNaN = true;
                    else if (float.IsInfinity(feats[i])) hasInf = true;
                }

                samples.Add(new MlSampleBinary
                {
                    Label = r.FactMicroUp,
                    Features = feats
                });
            }

            if (hasNaN || hasInf)
            {
                throw new InvalidOperationException(
                    $"[2stage-micro] датасет микро-слоя содержит некорректные значения признаков (NaN={hasNaN}, Inf={hasInf}).");
            }

            var data = ml.Data.LoadFromEnumerable(samples);

            var options = new LightGbmBinaryTrainer.Options
            {
                NumberOfLeaves = 12,
                NumberOfIterations = 70,
                LearningRate = 0.07f,
                MinimumExampleCountPerLeaf = 15,
                Seed = 42,
                NumberOfThreads = Environment.ProcessorCount
            };

            try
            {
                var pipe = ml.BinaryClassification.Trainers.LightGbm(options);
                var model = pipe.Fit(data);

                Console.WriteLine(
                    $"[2stage-micro] обучено на {flats.Count} REAL микро-днях " +
                    $"(up={upBalanced.Count}, down={dnBalanced.Count}, featDim={featureDim})");

                return model;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "[2stage-micro] LightGBM не смог обучить микро-модель при корректном датасете. " +
                    $"flats={flats.Count}, up={upBalanced.Count}, down={dnBalanced.Count}, featDim={featureDim ?? -1}.",
                    ex);
            }
        }

        private static DateTime DeriveMaxBaselineExitUtc(IReadOnlyList<LabeledCausalRow> rows, TimeZoneInfo nyTz)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (rows.Count == 0) throw new ArgumentException("rows must be non-empty.", nameof(rows));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));

            bool hasAny = false;
            DateTime maxExitUtc = default;

            for (int i = 0; i < rows.Count; i++)
            {
                var entryUtc = rows[i].EntryUtc;
                var entryUtcDt = entryUtc.Value;

                if (entryUtcDt.Kind != DateTimeKind.Utc)
                {
                    throw new InvalidOperationException(
                        $"[2stage-micro] entryUtc must be UTC. idx={i}, date={entryUtcDt:O}, kind={entryUtcDt.Kind}.");
                }

                // Weekend-check здесь не нужен: NyTradingEntryUtc по контракту не может быть weekend.
                // При нарушении инварианта NyWindowing.ComputeBaselineExitUtc(NyTradingEntryUtc, ...) упадёт fail-fast.
                var exitUtc = NyWindowing.ComputeBaselineExitUtc(entryUtc, nyTz).Value;

                if (!hasAny || exitUtc > maxExitUtc)
                {
                    maxExitUtc = exitUtc;
                    hasAny = true;
                }
            }

            if (!hasAny)
                throw new InvalidOperationException("[2stage-micro] failed to derive max baseline-exit: no working-day entries.");

            return maxExitUtc;
        }
    }
}

