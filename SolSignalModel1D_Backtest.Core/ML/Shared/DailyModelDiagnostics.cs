using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Analytics.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.ML.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML.Shared
{
    /// <summary>
    /// Диагностика дневных моделей (move / dir-normal / dir-down / micro-flat):
    /// считает PFI и печатает таблички в консоль.
    /// </summary>
    public static class DailyModelDiagnostics
    {
        public static void LogFeatureImportanceOnDailyModels(
            ModelBundle bundle,
            IReadOnlyList<LabeledCausalRow> evalRows,
            string datasetTag = "oos")
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            if (evalRows == null) throw new ArgumentNullException(nameof(evalRows));

            if (evalRows.Count == 0)
            {
                Console.WriteLine($"[pfi:daily: {datasetTag}] empty dataset, nothing to analyze.");
                return;
            }

            // Период печатаем по day-key (00:00 UTC), без DateTime-extensions.
            var minDate = evalRows.Min(r => ToCausalDayUtc(r.Causal.EntryDayKeyUtc.Value));
            var maxDate = evalRows.Max(r => ToCausalDayUtc(r.Causal.EntryDayKeyUtc.Value));
            Console.WriteLine($"[pfi:daily: {datasetTag}] rows={evalRows.Count}, period={minDate:yyyy-MM-dd}..{maxDate:yyyy-MM-dd}");

            // Единая логика разбиения на датасеты как в обучении (move/dir-normal/dir-down).
            DailyTrainingDataBuilder.Build(
                trainRows: evalRows,
                balanceMove: false,
                balanceDir: false,
                balanceTargetFrac: 0.5,
                moveTrainRows: out var moveRows,
                dirNormalRows: out var dirNormalRows,
                dirDownRows: out var dirDownRows);

            var ml = bundle.MlCtx ?? new MLContext(seed: 42);

            // ===== PFI: move (non-flat vs flat) =====
            if (bundle.MoveModel != null && moveRows.Count > 0)
            {
                var moveData = ml.Data.LoadFromEnumerable(
                    moveRows.Select(r => new MlSampleBinary
                    {
                        Label = r.TrueLabel != 1,
                        Features = MlTrainingUtils.ToFloatFixed(r.Causal.FeaturesVector)
                    })
                );

                FeatureImportanceAnalyzer.LogBinaryFeatureImportance(
                    ml, bundle.MoveModel, moveData, DailyFeatureSchema.Names, tag: $"{datasetTag}: move");
            }
            else
            {
                Console.WriteLine($"[pfi:daily: {datasetTag}] move-model or data is empty, skip.");
            }

            // ===== PFI: dir-normal (up vs down) вне down-regime =====
            if (bundle.DirModelNormal != null && dirNormalRows.Count > 0)
            {
                var dirNormalData = ml.Data.LoadFromEnumerable(
                    dirNormalRows.Select(r => new MlSampleBinary
                    {
                        Label = r.TrueLabel == 2,
                        Features = MlTrainingUtils.ToFloatFixed(r.Causal.FeaturesVector)
                    })
                );

                FeatureImportanceAnalyzer.LogBinaryFeatureImportance(
                    ml, bundle.DirModelNormal, dirNormalData, DailyFeatureSchema.Names, tag: $"{datasetTag}: dir-normal");
            }
            else
            {
                Console.WriteLine($"[pfi:daily: {datasetTag}] dir-normal: no model or no eval-rows, skip.");
            }

            // ===== PFI: dir-down (up vs down) внутри down-regime =====
            if (bundle.DirModelDown != null && dirDownRows.Count > 0)
            {
                var dirDownData = ml.Data.LoadFromEnumerable(
                    dirDownRows.Select(r => new MlSampleBinary
                    {
                        Label = r.TrueLabel == 2,
                        Features = MlTrainingUtils.ToFloatFixed(r.Causal.FeaturesVector)
                    })
                );

                FeatureImportanceAnalyzer.LogBinaryFeatureImportance(
                    ml, bundle.DirModelDown, dirDownData, DailyFeatureSchema.Names, tag: $"{datasetTag}: dir-down");
            }
            else
            {
                Console.WriteLine($"[pfi:daily: {datasetTag}] dir-down: no model or no eval-rows, skip.");
            }

            // ===== PFI: micro-flat (microUp vs microDown) =====
            if (bundle.MicroFlatModel != null)
            {
                var microRows = evalRows
                    .Where(r => r.FactMicroUp || r.FactMicroDown)
                    .ToList();

                if (microRows.Count >= 10)
                {
                    var microData = ml.Data.LoadFromEnumerable(
                        microRows.Select(r => new MlSampleBinary
                        {
                            Label = r.FactMicroUp,
                            Features = MlTrainingUtils.ToFloatFixed(r.Causal.FeaturesVector)
                        })
                    );

                    FeatureImportanceAnalyzer.LogBinaryFeatureImportance(
                        ml, bundle.MicroFlatModel, microData, MicroFeatureSchema.Names, tag: $"{datasetTag}: micro-flat");
                }
                else
                {
                    Console.WriteLine($"[pfi:daily: {datasetTag}] micro: too few micro-rows ({microRows.Count}), skip.");
                }
            }
            else
            {
                Console.WriteLine($"[pfi:daily: {datasetTag}] MicroFlatModel == null, skip micro layer PFI.");
            }
        }

        private static DateTime ToCausalDayUtc(DateTime tUtc)
        {
            if (tUtc.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"[pfi:daily] expected UTC day-key, got Kind={tUtc.Kind}, t={tUtc:O}.");

            return DateTime.SpecifyKind(tUtc.Date, DateTimeKind.Utc);
        }
    }
}
