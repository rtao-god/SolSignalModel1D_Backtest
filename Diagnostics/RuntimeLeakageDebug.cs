using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Diagnostics
{
    internal static class RuntimeLeakageDebug
    {
        public static void PrintDailyModelTrainOosProbe(
            IReadOnlyList<BacktestRecord> records,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
            TimeZoneInfo nyTz,
            int boundarySampleCount = 2)
        {
            if (records == null || records.Count == 0)
            {
                Console.WriteLine("[leak-probe] records is null or empty; nothing to probe.");
                return;
            }

            if (nyTz == null)
            {
                Console.WriteLine("[leak-probe] nyTz is null; probe is not meaningful.");
                return;
            }

            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));

            var ordered = records
                .OrderBy(r => r.Causal.EntryUtc.Value)
                .ToList();

            var split = NyTrainSplit.SplitByBaselineExit(
                ordered: ordered,
                entrySelector: r => r.Causal.EntryUtc,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                nyTz: nyTz);

            var train = split.Train;
            var oos = split.Oos;

            if (split.Excluded.Count > 0)
            {
                Console.WriteLine(
                    $"[leak-probe] WARNING: excluded={split.Excluded.Count} days (baseline-exit undefined by contract). " +
                    "Эти дни не учитываются ни в train, ни в OOS.");
            }

            bool TryGetExitDayKeyUtc(BacktestRecord r, out ExitDayKeyUtc exitDayKeyUtc)
            {
                var entryUtc = new EntryUtc(r.Causal.EntryUtc.Value);
                if (!NyWindowing.TryComputeBaselineExitUtc(entryUtc, nyTz, out var exitUtc))
                {
                    exitDayKeyUtc = default;
                    return false;
                }

                exitDayKeyUtc = ExitDayKeyUtc.FromBaselineExitUtcOrThrow(exitUtc.Value);
                return true;
            }

            (int total, int correct, double acc) Acc(IReadOnlyList<BacktestRecord> xs)
            {
                if (xs == null) throw new ArgumentNullException(nameof(xs));
                if (xs.Count == 0)
                    return (0, 0, double.NaN);

                int correct = 0;

                for (int i = 0; i < xs.Count; i++)
                {
                    var r = xs[i];
                    if (r.TrueLabel == r.Causal.PredLabel)
                        correct++;
                }

                double accVal = (double)correct / xs.Count;
                return (xs.Count, correct, accVal);
            }

            var trainAcc = Acc(train);
            var oosAcc = Acc(oos);

            DateTime? trainMaxExitDay = null;
            DateTime? oosMinExitDay = null;
            int exitBeforeEntryCount = 0;
            DateTime? exitBeforeEntrySample = null;

            void TrackExitDays(IReadOnlyList<BacktestRecord> xs, bool isTrain)
            {
                for (int i = 0; i < xs.Count; i++)
                {
                    var r = xs[i];
                    if (!TryGetExitDayKeyUtc(r, out var exitDayKey))
                        continue;

                    var exitDay = exitDayKey.Value;
                    var entryDay = r.Causal.EntryDayKeyUtc.Value;

                    if (exitDay < entryDay)
                    {
                        exitBeforeEntryCount++;
                        if (!exitBeforeEntrySample.HasValue)
                            exitBeforeEntrySample = r.Causal.EntryUtc.Value;
                    }

                    if (isTrain)
                    {
                        if (!trainMaxExitDay.HasValue || exitDay > trainMaxExitDay.Value)
                            trainMaxExitDay = exitDay;
                    }
                    else
                    {
                        if (!oosMinExitDay.HasValue || exitDay < oosMinExitDay.Value)
                            oosMinExitDay = exitDay;
                    }
                }
            }

            TrackExitDays(train, isTrain: true);
            TrackExitDays(oos, isTrain: false);

            Console.WriteLine(
                $"[leak-probe] trainUntil(exit-day-key) = {trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}, totalRecords = {ordered.Count}");

            if (trainMaxExitDay.HasValue && trainMaxExitDay.Value > trainUntilExitDayKeyUtc.Value)
            {
                Console.WriteLine(
                    $"[leak-probe] ПОДОЗРЕНИЕ: max exit-day-key в TRAIN превышает границу. " +
                    $"maxTrainExitDayKey={trainMaxExitDay.Value:yyyy-MM-dd}, trainUntil={trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}");
            }

            if (oosMinExitDay.HasValue && oosMinExitDay.Value <= trainUntilExitDayKeyUtc.Value)
            {
                Console.WriteLine(
                    $"[leak-probe] ПОДОЗРЕНИЕ: min exit-day-key в OOS не превышает границу. " +
                    $"minOosExitDayKey={oosMinExitDay.Value:yyyy-MM-dd}, trainUntil={trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}");
            }

            if (exitBeforeEntryCount > 0)
            {
                Console.WriteLine(
                    $"[leak-probe] ПОДОЗРЕНИЕ: найден exit-day-key раньше entry-day-key. " +
                    $"count={exitBeforeEntryCount}, sampleEntryUtc={exitBeforeEntrySample:O}");
            }

            Console.WriteLine(
                $"[leak-probe] TRAIN: count={trainAcc.total}, correct={trainAcc.correct}, acc={trainAcc.acc:P2}");

            if (oos.Count == 0)
            {
                Console.WriteLine("[leak-probe] OOS: count=0 (нет дней после границы по baseline-exit контракту)");
            }
            else
            {
                Console.WriteLine(
                    $"[leak-probe] OOS:   count={oosAcc.total}, correct={oosAcc.correct}, acc={oosAcc.acc:P2}");
            }

            if (!double.IsNaN(trainAcc.acc) && !double.IsNaN(oosAcc.acc))
            {
                double gap = trainAcc.acc - oosAcc.acc;
                if (trainAcc.acc >= 0.90 && oosAcc.acc <= 0.60 && gap >= 0.25)
                {
                    Console.WriteLine(
                        $"[leak-probe] ПОДОЗРЕНИЕ: большой разрыв точности train/oos " +
                        $"(train={trainAcc.acc:P2}, oos={oosAcc.acc:P2}, gap={gap:P2}).");
                }
            }

            void PrintRow(string kind, BacktestRecord r)
            {
                var c = r.Causal;
                var dayKey = c.EntryDayKeyUtc.Value;
                string exitDayKeyText = TryGetExitDayKeyUtc(r, out var exitDayKey)
                    ? exitDayKey.Value.ToString("yyyy-MM-dd")
                    : "<excluded>";

                Console.WriteLine(
                    $"[leak-probe] {kind} {dayKey:yyyy-MM-dd} " +
                    $"exitDayKey={exitDayKeyText} " +
                    $"true={r.TrueLabel} pred={c.PredLabel} " +
                    $"microUp={c.PredMicroUp} microDown={c.PredMicroDown} " +
                    $"minMove={c.MinMove:0.000}");
            }

            var trainSample = train
                .OrderByDescending(r => r.Causal.EntryUtc.Value)
                .Take(boundarySampleCount)
                .OrderBy(r => r.Causal.EntryUtc.Value)
                .ToList();

            var oosSample = oos
                .OrderBy(r => r.Causal.EntryUtc.Value)
                .Take(boundarySampleCount)
                .ToList();

            Console.WriteLine("[leak-probe] Sample near boundary (TRAIN → OOS):");

            if (trainSample.Count == 0)
            {
                Console.WriteLine("[leak-probe]   (no train rows)");
            }
            else
            {
                foreach (var r in trainSample)
                    PrintRow("T  ", r);
            }

            if (oosSample.Count == 0)
            {
                Console.WriteLine("[leak-probe]   (no OOS rows)");
            }
            else
            {
                foreach (var r in oosSample)
                    PrintRow("OOS", r);
            }
        }
    }
}

