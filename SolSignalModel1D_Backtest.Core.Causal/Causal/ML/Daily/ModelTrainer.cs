using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;
using SolSignalModel1D_Backtest.Core.Causal.ML.Utils;

namespace SolSignalModel1D_Backtest.Core.Causal.Causal.ML.Daily
{
    public sealed class ModelTrainer
    {
        public bool DisableMoveModel { get; set; }
        public bool DisableDirNormalModel { get; set; }
        public bool DisableDirDownModel { get; set; }
        public bool DisableMicroFlatModel { get; set; }

        private readonly int _gbmThreads = Math.Max(1, Environment.ProcessorCount - 1);
        private readonly MLContext _ml = new MLContext(seed: 42);

        private static readonly bool BalanceMove = false;
        private static readonly bool BalanceDir = true;
        private const double BalanceTargetFrac = 0.70;

        public ModelBundle TrainAll(
            IReadOnlyList<LabeledCausalRow> trainRows,
            HashSet<EntryDayKeyUtc>? dayKeysToExclude = null)
        {
            if (trainRows == null) throw new ArgumentNullException(nameof(trainRows));
            if (trainRows.Count == 0)
                throw new InvalidOperationException("TrainAll: empty trainRows - нечего обучать.");

            var ordered = trainRows
                .OrderBy(EntryDayKeyUtcValue)
                .ToList();

            if (dayKeysToExclude != null && dayKeysToExclude.Count > 0)
                ordered = ordered.Where(r => !dayKeysToExclude.Contains(r.Causal.EntryDayKeyUtc)).ToList();

            if (ordered.Count == 0)
                throw new InvalidOperationException("TrainAll: all rows excluded by dayKeysToExclude.");

            var moveRows = ordered;
            var dirRows = ordered.Where(r => r.TrueLabel != 1).ToList();

            var dirNormalRows = dirRows.Where(r => !r.Causal.RegimeDown).ToList();
            var dirDownRows = dirRows.Where(r => r.Causal.RegimeDown).ToList();

            if (BalanceMove)
            {
                moveRows = MlTrainingUtils.OversampleBinary(
                    src: moveRows,
                    isPositive: r => r.TrueLabel != 1,
                    dateSelector: EntryDayKeyUtcValue,
                    targetFrac: BalanceTargetFrac);
            }

            if (BalanceDir)
            {
                dirNormalRows = MlTrainingUtils.OversampleBinary(
                    src: dirNormalRows,
                    isPositive: r => r.TrueLabel == 2,
                    dateSelector: EntryDayKeyUtcValue,
                    targetFrac: BalanceTargetFrac);

                dirDownRows = MlTrainingUtils.OversampleBinary(
                    src: dirDownRows,
                    isPositive: r => r.TrueLabel == 2,
                    dateSelector: EntryDayKeyUtcValue,
                    targetFrac: BalanceTargetFrac);
            }

            ITransformer? moveModel = null;

            if (DisableMoveModel)
            {
                Console.WriteLine("[2stage] move-model DISABLED by flag, skipped training");
            }
            else
            {
                if (moveRows.Count == 0)
                {
                    Console.WriteLine("[2stage] move-model: train rows = 0, skipping");
                }
                else
                {
                    var moveData = _ml.Data.LoadFromEnumerable(
                        moveRows.Select(r => new MlSampleBinary
                        {
                            Label = r.TrueLabel != 1,
                            Features = MlTrainingUtils.ToFloatFixed(r.Causal.FeaturesVector)
                        }));

                    var movePipe = _ml.BinaryClassification.Trainers.LightGbm(
                        new LightGbmBinaryTrainer.Options
                        {
                            NumberOfLeaves = 16,
                            NumberOfIterations = 90,
                            LearningRate = 0.07f,
                            MinimumExampleCountPerLeaf = 20,
                            Seed = 42,
                            NumberOfThreads = _gbmThreads
                        });

                    moveModel = movePipe.Fit(moveData);
                    Console.WriteLine($"[2stage] move-model trained on {moveRows.Count} rows");
                }
            }

            ITransformer? dirNormalModel = null;
            ITransformer? dirDownModel = null;

            if (DisableDirNormalModel)
            {
                Console.WriteLine("[2stage] dir-normal DISABLED by flag, skipped training");
            }
            else
            {
                dirNormalModel = BuildDirModel(dirNormalRows, "dir-normal");
            }

            if (DisableDirDownModel)
            {
                Console.WriteLine("[2stage] dir-down DISABLED by flag, skipped training");
            }
            else
            {
                dirDownModel = BuildDirModel(dirDownRows, "dir-down");
            }

            ITransformer? microModel = null;

            if (DisableMicroFlatModel)
            {
                Console.WriteLine("[2stage] micro-flat DISABLED by flag, skipped training");
            }
            else
            {
                var microRows = ordered
                    .Where(r => r.TrueLabel == 1 && r.MicroTruth.HasValue)
                    .ToList();

                if (microRows.Count < 40)
                {
                    Console.WriteLine($"[2stage] micro-flat: too few rows ({microRows.Count}), skipping");
                    microModel = null;
                }
                else
                {
                    var microData = _ml.Data.LoadFromEnumerable(
                        microRows.Select(r => new MlSampleBinary
                        {
                            Label = r.MicroTruth.HasValue && r.MicroTruth.Value == MicroTruthDirection.Up,
                            Features = MlTrainingUtils.ToFloatFixed(r.Causal.FeaturesVector)
                        }));

                    var microPipe = _ml.BinaryClassification.Trainers.LightGbm(
                        new LightGbmBinaryTrainer.Options
                        {
                            NumberOfLeaves = 8,
                            NumberOfIterations = 60,
                            LearningRate = 0.05f,
                            MinimumExampleCountPerLeaf = 25,
                            Seed = 42,
                            NumberOfThreads = _gbmThreads
                        });

                    microModel = microPipe.Fit(microData);
                    Console.WriteLine($"[2stage] micro-flat trained on {microRows.Count} rows");
                }
            }

            return new ModelBundle
            {
                MoveModel = moveModel,
                DirModelNormal = dirNormalModel,
                DirModelDown = dirDownModel,
                MicroFlatModel = microModel,
                MlCtx = _ml
            };
        }

        private ITransformer? BuildDirModel(IReadOnlyList<LabeledCausalRow> rows, string tag)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            if (rows.Count < 40)
            {
                Console.WriteLine($"[2stage] {tag}: мало строк ({rows.Count}), скипаем");
                return null;
            }

            var data = _ml.Data.LoadFromEnumerable(
                rows.Select(r => new MlSampleBinary
                {
                    Label = r.TrueLabel == 2,
                    Features = MlTrainingUtils.ToFloatFixed(r.Causal.FeaturesVector)
                }));

            var pipe = _ml.BinaryClassification.Trainers.LightGbm(
                new LightGbmBinaryTrainer.Options
                {
                    NumberOfLeaves = 16,
                    NumberOfIterations = 90,
                    LearningRate = 0.07f,
                    MinimumExampleCountPerLeaf = 15,
                    Seed = 42,
                    NumberOfThreads = _gbmThreads
                });

            var model = pipe.Fit(data);
            Console.WriteLine($"[2stage] {tag}: trained on {rows.Count} rows");
            return model;
        }

        private static DateTime EntryDayKeyUtcValue(LabeledCausalRow r) => r.Causal.EntryDayKeyUtc.Value;
    }
}
