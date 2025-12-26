using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Utils;
using SolSignalModel1D_Backtest.Core.Time;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSignalModel1D_Backtest
{
    public partial class Program
    {
        private static void RunDailyPfi(List<LabeledCausalRow> allRows)
        {
            if (allRows == null) throw new ArgumentNullException(nameof(allRows));
            if (allRows.Count == 0)
            {
                Console.WriteLine("[pfi:daily] allRows is empty, skip.");
                return;
            }

            var ordered = allRows
                .OrderBy(r => r.EntryUtc.Value)
                .ToList();

            var trainUntil = new TrainUntilUtc(_trainUntilUtc);

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: ordered,
                entrySelector: static r => new EntryUtc(r.EntryUtc.Value),
                trainUntilExitDayKeyUtc: trainUntil.ExitDayKeyUtc,
                nyTz: NyTz);

            var dailyTrainRows = split.Train is List<LabeledCausalRow> tl ? tl : split.Train.ToList();
            var dailyOosRows = split.Oos is List<LabeledCausalRow> ol ? ol : split.Oos.ToList();

            if (dailyTrainRows.Count < 50)
            {
                Console.WriteLine($"[pfi:daily] not enough train rows for PFI (count={dailyTrainRows.Count}), skip.");
                return;
            }

            var dailyTrainer = new ModelTrainer();
            var bundle = dailyTrainer.TrainAll(dailyTrainRows, dayKeysToExclude: null);

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
