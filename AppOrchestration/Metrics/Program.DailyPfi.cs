using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Utils;
using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest
{
    public partial class Program
    {
        private static void RunDailyPfi(List<LabeledCausalRow> allRows)
        {
            var boundary = new TrainBoundary(_trainUntilUtc, NyTz);
            var split = boundary.Split(allRows, r => r.EntryUtc.Value);

            var dailyTrainRows = split.Train;
            var dailyOosRows = split.Oos;

            if (dailyTrainRows.Count < 50)
            {
                Console.WriteLine($"[pfi:daily] not enough train rows for PFI (count={dailyTrainRows.Count}), skip.");
                return;
            }

            var dailyTrainer = new ModelTrainer();
            var bundle = dailyTrainer.TrainAll(dailyTrainRows, datesToExclude: null);

            DailyModelDiagnostics.LogFeatureImportanceOnDailyModels(bundle, dailyTrainRows, "train");

            if (dailyOosRows.Count > 0)
            {
                var tag = dailyOosRows.Count >= 50 ? "oos" : "oos-small";
                DailyModelDiagnostics.LogFeatureImportanceOnDailyModels(bundle, dailyOosRows, tag);
            }
            else
            {
                Console.WriteLine("[pfi:daily] no OOS rows after train boundary (baseline-exit), skip oos PFI.");
            }

            if (split.Excluded.Count > 0)
            {
                Console.WriteLine(
                    $"[pfi:daily] WARNING: excluded={split.Excluded.Count} rows have undefined baseline-exit and were ignored.");
            }
        }
    }
}
