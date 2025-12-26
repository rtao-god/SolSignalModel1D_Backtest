using Microsoft.ML;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.Analytics.ML;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.ML.SL;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.ML.Diagnostics.SL
{
    /// <summary>
    /// Диагностика SL-модели (SlFirstTrainer):
    /// строит/использует модель на готовых SlHitSample и считает PFI.
    /// </summary>
    public static class SlModelDiagnostics
    {
        private sealed class SlPfiRow
        {
            public bool Label { get; set; }

            [VectorType(SlSchema.FeatureCount)]
            public float[] Features { get; set; } = new float[SlSchema.FeatureCount];
        }

        public static void LogFeatureImportanceOnSlModel(
            List<SlHitSample> samples,
            string datasetTag,
            ITransformer? modelOverride = null,
            string[]? featureNames = null)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));

            if (samples.Count < 20)
            {
                Console.WriteLine($"[pfi:sl:{datasetTag}] too few samples ({samples.Count}), skip.");
                return;
            }

            var minDate = samples.Min(s => s.EntryUtc);
            var maxDate = samples.Max(s => s.EntryUtc);

            int pos = samples.Count(s => s.Label);
            int neg = samples.Count - pos;

            Console.WriteLine(
                $"[pfi:sl:{datasetTag}] samples={samples.Count}, pos={pos}, neg={neg}, " +
                $"period={minDate:yyyy-MM-dd}..{maxDate:yyyy-MM-dd}");

            ITransformer model;
            if (modelOverride != null)
            {
                model = modelOverride;
            }
            else
            {
                var trainer = new SlFirstTrainer();
                model = trainer.Train(samples, asOfUtc: maxDate);
            }

            var ml = new MLContext(seed: 42);

            var names = featureNames ?? SlFeatureSchema.Names;

            if (names.Length != SlSchema.FeatureCount)
            {
                throw new InvalidOperationException(
                    $"[pfi:sl:{datasetTag}] featureNames length mismatch: names={names.Length}, SlSchema.FeatureCount={SlSchema.FeatureCount}.");
            }

            var data = ml.Data.LoadFromEnumerable(
                samples.Select(s => new SlPfiRow
                {
                    Label = s.Label,
                    Features = s.Features
                })
            );

            FeatureImportanceAnalyzer.LogBinaryFeatureImportance(
                ml,
                model,
                data,
                names,
                tag: datasetTag);
        }
    }
}
