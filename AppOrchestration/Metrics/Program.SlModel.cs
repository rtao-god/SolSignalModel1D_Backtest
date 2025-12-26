using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using BacktestRecord = SolSignalModel1D_Backtest.Core.Omniscient.Data.BacktestRecord;

namespace SolSignalModel1D_Backtest
{
    public partial class Program
    {
        private static void RunSlModelOffline(
            List<LabeledCausalRow> allRows,
            List<BacktestRecord> records,
            List<Candle1h> sol1h,
            List<Candle1m> sol1m,
            List<Candle6h> solAll6h)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (records.Count == 0)
                throw new InvalidOperationException("[sl-offline] records is empty.");

            var orderedRecords = records
                .OrderBy(r => r.Causal.EntryUtc.Value)
                .ToList();

            var split = NyTrainSplit.SplitByBaselineExitStrict(
                ordered: orderedRecords,
                entrySelector: static r => r.Causal.EntryUtc,
                trainUntilUtc: new TrainUntilUtc(_trainUntilUtc),
                nyTz: NyTz,
                tag: "sl.records");

            if (split.Train.Count < 50)
            {
                throw new InvalidOperationException(
                    $"[sl-offline] SL train subset too small (count={split.Train.Count}). " +
                    $"trainUntilUtc={_trainUntilUtc:O}.");
            }

            TrainAndApplySlModelOffline(
                trainRecords: split.Train,
                records: records,
                sol1h: sol1h,
                sol1m: sol1m,
                solAll6h: solAll6h
            );
        }
    }
}
