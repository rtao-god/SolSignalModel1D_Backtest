using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.ModelStats;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.ModelStats;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Snapshots.Aggregation;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Snapshots.Micro;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Snapshots.ModelStats;
using SolSignalModel1D_Backtest.Core.Omniscient.Causal.Analytics.Backtest.Adapters;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Diagnostics
{
    public static class BacktestDiagnosticsSnapshotBuilder
    {
        public static BacktestDiagnosticsSnapshot Build(
            IReadOnlyList<BacktestRecord> records,
            IReadOnlyList<Candle1m> sol1m,
            double dailyTpPct,
            double dailySlPct,
            TimeZoneInfo nyTz,
            TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc,
            int recentDays,
            int debugLastDays,
            ModelRunKind runKind)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (sol1m == null) throw new ArgumentNullException(nameof(sol1m));
            if (nyTz == null) throw new ArgumentNullException(nameof(nyTz));
            if (trainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof(trainUntilExitDayKeyUtc));
            if (recentDays <= 0) throw new ArgumentOutOfRangeException(nameof(recentDays), "recentDays must be > 0.");
            if (debugLastDays <= 0) throw new ArgumentOutOfRangeException(nameof(debugLastDays), "debugLastDays must be > 0.");
            if (records.Count == 0)
                throw new InvalidOperationException("[diag] records=0: невозможно построить диагностику без данных.");

            var ordered = records
                .OrderBy(r => r.EntryUtc.Value)
                .ToList();

            var eligibleRecords = new List<BacktestRecord>(ordered.Count);
            var excludedRecords = new List<BacktestRecord>();
            var trainRecords = new List<BacktestRecord>(ordered.Count);
            var oosRecords = new List<BacktestRecord>(Math.Min(ordered.Count, 512));

            var eligibleAgg = new List<BacktestAggRow>(ordered.Count);
            var excludedAgg = new List<BacktestAggRow>();
            var trainAgg = new List<BacktestAggRow>(ordered.Count);
            var oosAgg = new List<BacktestAggRow>(Math.Min(ordered.Count, 512));

            var exitDayKeyByRecord = new Dictionary<BacktestRecord, ExitDayKeyUtc>(ordered.Count);

            foreach (var r in ordered)
            {
                var entryUtc = r.EntryUtc;
                var cls = NyTrainSplit.ClassifyByBaselineExit(
                    entryUtc: entryUtc,
                    trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                    nyTz: nyTz,
                    baselineExitDayKeyUtc: out var exitDayKeyUtc);

                var agg = r.ToAggRow();

                if (cls == NyTrainSplit.EntryClass.Excluded)
                {
                    excludedRecords.Add(r);
                    excludedAgg.Add(agg);
                    continue;
                }

                exitDayKeyByRecord[r] = exitDayKeyUtc;
                eligibleRecords.Add(r);
                eligibleAgg.Add(agg);

                if (cls == NyTrainSplit.EntryClass.Train)
                {
                    trainRecords.Add(r);
                    trainAgg.Add(agg);
                }
                else
                {
                    oosRecords.Add(r);
                    oosAgg.Add(agg);
                }
            }

            var sets = new AggregationInputSets
            {
                Boundary = new TrainBoundaryMeta(trainUntilExitDayKeyUtc),
                Eligible = eligibleAgg,
                Excluded = excludedAgg,
                Train = trainAgg,
                Oos = oosAgg
            };

            var probsSnap = AggregationProbsSnapshotBuilder.Build(sets, recentDays: recentDays, debugLastDays: debugLastDays);
            var metricsSnap = AggregationMetricsSnapshotBuilder.Build(sets, recentDays: recentDays);
            var microSnap = MicroStatsSnapshotBuilder.Build(sets.Eligible);

            var sol1mSorted = sol1m
                .OrderBy(m => m.OpenTimeUtc)
                .ToList();

            var modelStats = BacktestModelStatsMultiSnapshotBuilder.BuildFromSplit(
                trainRecords: trainRecords,
                oosRecords: oosRecords,
                sol1m: sol1mSorted,
                nyTz: nyTz,
                dailyTpPct: dailyTpPct,
                dailySlPct: dailySlPct,
                trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                recentDays: recentDays,
                runKind: runKind);

            var fullRecords = new List<BacktestRecord>(trainRecords.Count + oosRecords.Count);
            fullRecords.AddRange(trainRecords);
            fullRecords.AddRange(oosRecords);
            fullRecords = fullRecords.OrderBy(r => r.EntryDayKeyUtc.Value).ToList();

            var recentRecords = BuildRecent(fullRecords, recentDays);

            var (shuffleAccPct, shuffleN) = ComputeShuffleSanity(recentRecords);

            var segments = new List<BacktestDiagnosticsSegmentSnapshot>
            {
                BuildSegmentSnapshot(
                    kind: BacktestDiagnosticsSegmentKind.Train,
                    label: $"Train (exit<= {NyTrainSplit.ToIsoDate(trainUntilExitDayKeyUtc)})",
                    records: trainRecords,
                    exitDayKeyByRecord: exitDayKeyByRecord,
                    modelStats: FindModelStatsSegment(modelStats, ModelStatsSegmentKind.TrainOnly),
                    sol1m: sol1mSorted,
                    dailyTpPct: dailyTpPct,
                    dailySlPct: dailySlPct,
                    nyTz: nyTz),

                BuildSegmentSnapshot(
                    kind: BacktestDiagnosticsSegmentKind.Oos,
                    label: $"OOS (exit>  {NyTrainSplit.ToIsoDate(trainUntilExitDayKeyUtc)})",
                    records: oosRecords,
                    exitDayKeyByRecord: exitDayKeyByRecord,
                    modelStats: FindModelStatsSegment(modelStats, ModelStatsSegmentKind.OosOnly),
                    sol1m: sol1mSorted,
                    dailyTpPct: dailyTpPct,
                    dailySlPct: dailySlPct,
                    nyTz: nyTz),

                BuildSegmentSnapshot(
                    kind: BacktestDiagnosticsSegmentKind.Recent,
                    label: $"Recent({recentDays}d)",
                    records: recentRecords,
                    exitDayKeyByRecord: exitDayKeyByRecord,
                    modelStats: FindModelStatsSegment(modelStats, ModelStatsSegmentKind.RecentWindow),
                    sol1m: sol1mSorted,
                    dailyTpPct: dailyTpPct,
                    dailySlPct: dailySlPct,
                    nyTz: nyTz),

                BuildSegmentSnapshot(
                    kind: BacktestDiagnosticsSegmentKind.Full,
                    label: "Full (eligible days)",
                    records: fullRecords,
                    exitDayKeyByRecord: exitDayKeyByRecord,
                    modelStats: FindModelStatsSegment(modelStats, ModelStatsSegmentKind.FullHistory),
                    sol1m: sol1mSorted,
                    dailyTpPct: dailyTpPct,
                    dailySlPct: dailySlPct,
                    nyTz: nyTz)
            };

            var coverage = BuildCoverage(
                eligibleRecords: eligibleRecords,
                excludedRecords: excludedRecords,
                sol1m: sol1mSorted,
                dailyTpPct: dailyTpPct,
                dailySlPct: dailySlPct,
                nyTz: nyTz);

            return new BacktestDiagnosticsSnapshot
            {
                Meta = new BacktestDiagnosticsMeta
                {
                    TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc,
                    RecentDays = recentDays,
                    DebugLastDays = debugLastDays,
                    ShuffleSanityAccuracyPct = shuffleAccPct,
                    ShuffleSanityN = shuffleN
                },
                Coverage = coverage,
                Segments = segments,
                AggregationProbs = probsSnap,
                AggregationMetrics = metricsSnap,
                MicroStats = microSnap,
                ModelStats = modelStats
            };
        }

        private static BacktestDiagnosticsCoverage BuildCoverage(
            IReadOnlyList<BacktestRecord> eligibleRecords,
            IReadOnlyList<BacktestRecord> excludedRecords,
            IReadOnlyList<Candle1m> sol1m,
            double dailyTpPct,
            double dailySlPct,
            TimeZoneInfo nyTz)
        {
            if (eligibleRecords == null) throw new ArgumentNullException(nameof(eligibleRecords));
            if (excludedRecords == null) throw new ArgumentNullException(nameof(excludedRecords));
            if (eligibleRecords.Count == 0)
                throw new InvalidOperationException("[diag] coverage: eligibleRecords=0.");

            var missing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int recordsTotal = eligibleRecords.Count + excludedRecords.Count;
            int microTruth = 0;
            int microGating = 0;
            int slScore = 0;
            int slLabel = 0;
            int slEval = 0;
            int truthDaily = 0;

            foreach (var r in eligibleRecords)
            {
                if (r.TrueLabel is >= 0 and <= 2)
                    truthDaily++;

                if (r.MicroTruth.HasValue)
                    microTruth++;
                else
                    AddMissing(missing, r.MicroTruth.MissingReason);

                if (r.PredLabel_Day == 1)
                    microGating++;

                if (r.SlProb.HasValue)
                    slScore++;
                else
                    AddMissing(missing, r.SlProb.MissingReason);

                if (r.SlHighDecision.HasValue)
                {
                    if (!r.SlProb.HasValue)
                    {
                        throw new InvalidOperationException(
                            $"[diag] SL availability mismatch in coverage at {r.EntryUtc.Value:O}. " +
                            $"slProb.HasValue={r.SlProb.HasValue}, slHigh.HasValue={r.SlHighDecision.HasValue}.");
                    }
                }
                else
                {
                    AddMissing(missing, r.SlHighDecision.MissingReason);
                }

                if (TryGetPredDirection(r, out var goLong, out var goShort))
                {
                    if (TryGetSlOutcome(r, sol1m, dailyTpPct, dailySlPct, nyTz, goLong, goShort, out _))
                    {
                        slLabel++;
                        if (r.SlProb.HasValue)
                            slEval++;
                    }
                    else
                    {
                        AddMissing(missing, MissingReasonCodes.No1mPath);
                    }
                }
                else
                {
                    AddMissing(missing, MissingReasonCodes.NoSignalDay);
                }
            }

            return new BacktestDiagnosticsCoverage
            {
                RecordsTotal = recordsTotal,
                RecordsExcludedByWindowing = excludedRecords.Count,
                TruthDailyLabelAvailable = truthDaily,
                MicroTruthAvailable = microTruth,
                MicroGatingDays = microGating,
                SlScoreAvailable = slScore,
                SlLabelAvailable = slLabel,
                SlEvalBase = slEval,
                MissingReasons = missing
            };
        }

        private static BacktestDiagnosticsSegmentSnapshot BuildSegmentSnapshot(
            BacktestDiagnosticsSegmentKind kind,
            string label,
            IReadOnlyList<BacktestRecord> records,
            IReadOnlyDictionary<BacktestRecord, ExitDayKeyUtc> exitDayKeyByRecord,
            BacktestModelStatsSegmentSnapshot? modelStats,
            IReadOnlyList<Candle1m> sol1m,
            double dailyTpPct,
            double dailySlPct,
            TimeZoneInfo nyTz)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (records.Count == 0)
                throw new InvalidOperationException($"[diag] empty segment '{label}'.");

            DateTime? entryFrom = null;
            DateTime? entryTo = null;
            DateTime? exitFrom = null;
            DateTime? exitTo = null;

            if (records.Count > 0)
            {
                entryFrom = records.Min(r => r.EntryDayKeyUtc.Value);
                entryTo = records.Max(r => r.EntryDayKeyUtc.Value);

                foreach (var r in records)
                {
                    if (!exitDayKeyByRecord.TryGetValue(r, out var exitDayKeyUtc))
                        throw new InvalidOperationException($"[diag] Missing exit day-key for record {r.EntryUtc.Value:O}.");

                    var exit = exitDayKeyUtc.Value;
                    if (!exitFrom.HasValue || exit < exitFrom.Value) exitFrom = exit;
                    if (!exitTo.HasValue || exit > exitTo.Value) exitTo = exit;
                }
            }

            var bases = ComputeSegmentBases(records, modelStats, sol1m, dailyTpPct, dailySlPct, nyTz);
            var missing = ComputeMissingBreakdown(records, sol1m, dailyTpPct, dailySlPct, nyTz);
            var componentStats = ComputeComponentStats(records);

            return new BacktestDiagnosticsSegmentSnapshot
            {
                Kind = kind,
                Label = label,
                EntryFromUtc = entryFrom,
                EntryToUtc = entryTo,
                ExitFromUtc = exitFrom,
                ExitToUtc = exitTo,
                RecordsCount = records.Count,
                Bases = bases,
                Missing = missing,
                ComponentStats = componentStats
            };
        }

        private static BacktestDiagnosticsSegmentBases ComputeSegmentBases(
            IReadOnlyList<BacktestRecord> records,
            BacktestModelStatsSegmentSnapshot? modelStats,
            IReadOnlyList<Candle1m> sol1m,
            double dailyTpPct,
            double dailySlPct,
            TimeZoneInfo nyTz)
        {
            if (modelStats == null)
                throw new InvalidOperationException("[diag] modelStats segment is missing.");

            int microTruth = records.Count(r => r.MicroTruth.HasValue);
            int microGating = records.Count(r => r.PredLabel_Day == 1);
            int slScore = records.Count(r => r.SlProb.HasValue);
            int slLabel = 0;
            int slEval = 0;

            int dailyEval = modelStats.Stats.Daily.OverallTotal;
            int trendEval = modelStats.Stats.Trend.OverallTotal;

            foreach (var r in records)
            {
                if (TryGetPredDirection(r, out var goLong, out var goShort))
                {
                    if (TryGetSlOutcome(r, sol1m, dailyTpPct, dailySlPct, nyTz, goLong, goShort, out _))
                    {
                        slLabel++;
                        if (r.SlProb.HasValue)
                            slEval++;
                    }
                }
            }

            return new BacktestDiagnosticsSegmentBases
            {
                NDailyEval = dailyEval,
                NTrendEval = trendEval,
                NMicroEval = microTruth,
                NMicroTruth = microTruth,
                NMicroGating = microGating,
                NSlEval = slEval,
                NSlScore = slScore,
                NSlLabel = slLabel
            };
        }

        private static BacktestDiagnosticsMissingBreakdown ComputeMissingBreakdown(
            IReadOnlyList<BacktestRecord> records,
            IReadOnlyList<Candle1m> sol1m,
            double dailyTpPct,
            double dailySlPct,
            TimeZoneInfo nyTz)
        {
            var missing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in records)
            {
                if (!r.MicroTruth.HasValue)
                    AddMissing(missing, r.MicroTruth.MissingReason);

                if (!r.SlProb.HasValue)
                    AddMissing(missing, r.SlProb.MissingReason);

                if (!r.SlHighDecision.HasValue)
                    AddMissing(missing, r.SlHighDecision.MissingReason);

                if (TryGetPredDirection(r, out var goLong, out var goShort))
                {
                    if (!TryGetSlOutcome(r, sol1m, dailyTpPct, dailySlPct, nyTz, goLong, goShort, out _))
                        AddMissing(missing, MissingReasonCodes.No1mPath);
                }
                else
                {
                    AddMissing(missing, MissingReasonCodes.NoSignalDay);
                }
            }

            return new BacktestDiagnosticsMissingBreakdown { Reasons = missing };
        }

        private static BacktestDiagnosticsComponentStatsSnapshot ComputeComponentStats(IReadOnlyList<BacktestRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (records.Count == 0)
                throw new InvalidOperationException("[diag-components] records=0: невозможно построить компонентные метрики.");

            var day = ComputeTriClassAccuracy(records, r => r.PredLabel_Day);
            var dayMicro = ComputeTriClassAccuracy(records, r => r.PredLabel_DayMicro);
            var total = ComputeTriClassAccuracy(records, r => r.PredLabel_Total);

            var move = ComputeMoveStats(records);
            var dir = ComputeDirStats(records);
            var micro = ComputeMicroStats(records);

            return new BacktestDiagnosticsComponentStatsSnapshot
            {
                Day = day,
                DayMicro = dayMicro,
                Total = total,
                Move = move,
                Dir = dir,
                Micro = micro
            };
        }

        private static (double? AccPct, int N) ComputeShuffleSanity(IReadOnlyList<BacktestRecord> records)
        {
            if (records == null || records.Count == 0)
                throw new InvalidOperationException("[diag] shuffle sanity: records=0.");

            var filtered = records
                .Where(r => r.TrueLabel is >= 0 and <= 2 && r.PredLabel is >= 0 and <= 2)
                .ToList();

            if (filtered.Count == 0)
                throw new InvalidOperationException("[diag] shuffle sanity: no valid labels/preds.");

            var labels = filtered
                .Select(r => r.TrueLabel)
                .ToArray();

            var rng = new Random(123);
            for (int i = labels.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (labels[i], labels[j]) = (labels[j], labels[i]);
            }

            int diag = 0;
            int n = filtered.Count;

            for (int i = 0; i < n; i++)
            {
                if (labels[i] == filtered[i].PredLabel)
                    diag++;
            }

            double accPct = (double)diag / n * 100.0;
            return (accPct, n);
        }

        private static TriClassComponentStats ComputeTriClassAccuracy(
            IReadOnlyList<BacktestRecord> records,
            Func<BacktestRecord, int> predSelector)
        {
            int n = 0;
            int correct = 0;

            foreach (var r in records)
            {
                int y = r.TrueLabel;
                int p = predSelector(r);

                if (y is < 0 or > 2) continue;
                if (p is < 0 or > 2) continue;

                n++;
                if (y == p) correct++;
            }

            if (n == 0)
                throw new InvalidOperationException("[diag-components] tri-class: no valid labels/preds.");

            double acc = (double)correct / n;

            return new TriClassComponentStats
            {
                N = n,
                Correct = correct,
                Accuracy = acc
            };
        }

        private static MoveComponentStats ComputeMoveStats(IReadOnlyList<BacktestRecord> records)
        {
            int n = 0;
            int correct = 0;
            int trueMove = 0;
            int trueFlat = 0;
            int predMove = 0;
            int predFlat = 0;

            foreach (var r in records)
            {
                bool pred = GetMovePredictionOrThrow(r);
                bool truth = r.TrueLabel != 1;

                n++;
                if (truth) trueMove++; else trueFlat++;
                if (pred) predMove++; else predFlat++;
                if (pred == truth) correct++;
            }

            if (n == 0)
                throw new InvalidOperationException("[diag-components] move: no records.");

            double acc = (double)correct / n;

            return new MoveComponentStats
            {
                N = n,
                Correct = correct,
                Accuracy = acc,
                TrueMove = trueMove,
                TrueFlat = trueFlat,
                PredMove = predMove,
                PredFlat = predFlat
            };
        }

        private static DirComponentStats ComputeDirStats(IReadOnlyList<BacktestRecord> records)
        {
            int n = 0;
            int correct = 0;
            int movePredTrue = 0;
            int movePredTrueFlatTruth = 0;

            foreach (var r in records)
            {
                if (!GetMovePredictionOrThrow(r))
                    continue;

                movePredTrue++;

                int y = r.TrueLabel;
                if (y == 1)
                {
                    movePredTrueFlatTruth++;
                    continue;
                }

                if (y != 0 && y != 2)
                    throw new InvalidOperationException($"[diag-components] unexpected TrueLabel={y} for dir model at {r.EntryUtc.Value:O}.");

                var predDir = GetDirPredictionOrThrow(r);
                if (predDir == null)
                    continue;

                bool truthUp = y == 2;
                n++;
                if (predDir.Value == truthUp)
                    correct++;
            }

            double acc = n > 0 ? (double)correct / n : double.NaN;
            if (n == 0)
                throw new InvalidOperationException("[diag-components] dir: no evaluated records.");

            return new DirComponentStats
            {
                N = n,
                Correct = correct,
                Accuracy = acc,
                MovePredTrue = movePredTrue,
                MoveTrueButTruthFlat = movePredTrueFlatTruth
            };
        }

        private static OptionalValue<MicroComponentStats> ComputeMicroStats(IReadOnlyList<BacktestRecord> records)
        {
            int n = 0;
            int correct = 0;
            int factMicroDays = 0;
            int predMicroDays = 0;

            foreach (var r in records)
            {
                if (r.TrueLabel != 1)
                    continue;

                if (!r.MicroTruth.HasValue)
                    continue;

                bool truthUp = r.MicroTruth.Value == MicroTruthDirection.Up;
                factMicroDays++;

                bool predUp = r.PredMicroUp;
                bool predDown = r.PredMicroDown;

                if (predUp && predDown)
                    throw new InvalidOperationException($"[diag-components] predMicroUp && predMicroDown одновременно на {r.EntryUtc.Value:O}.");

                if (!predUp && !predDown)
                    continue;

                predMicroDays++;
                n++;

                if ((predUp && truthUp) || (predDown && !truthUp))
                    correct++;
            }

            if (factMicroDays == 0)
                return OptionalValue<MicroComponentStats>.Missing(MissingReasonCodes.MicroNoTruth);
            if (predMicroDays == 0)
                return OptionalValue<MicroComponentStats>.Missing(MissingReasonCodes.MicroNoPredictions);
            if (n == 0)
                return OptionalValue<MicroComponentStats>.Missing(MissingReasonCodes.MicroNoEvaluated);

            double acc = (double)correct / n;
            double coverage = (double)predMicroDays / factMicroDays;

            return OptionalValue<MicroComponentStats>.Present(new MicroComponentStats
            {
                N = n,
                Correct = correct,
                Accuracy = acc,
                FactMicroDays = factMicroDays,
                PredMicroDays = predMicroDays,
                Coverage = coverage
            });
        }

        private static bool TryGetPredDirection(BacktestRecord r, out bool predUp, out bool predDown)
        {
            bool microUp = r.PredMicroUp == true;
            bool microDown = r.PredMicroDown == true;

            if (microUp && microDown)
                throw new InvalidOperationException($"[diag] microUp && microDown одновременно на {r.EntryUtc.Value:O}.");

            predUp = r.PredLabel == 2 || (r.PredLabel == 1 && microUp);
            predDown = r.PredLabel == 0 || (r.PredLabel == 1 && microDown);

            return predUp || predDown;
        }

        private static bool TryGetSlOutcome(
            BacktestRecord r,
            IReadOnlyList<Candle1m> allMinutesSorted,
            double tpPct,
            double slPct,
            TimeZoneInfo nyTz,
            bool goLong,
            bool goShort,
            out bool isSlDay)
        {
            isSlDay = false;

            if (!goLong && !goShort)
                return false;

            double entry = r.Entry;
            if (entry <= 0.0)
                return false;

            DateTime from = r.EntryUtc.Value;

            if (!NyWindowing.TryComputeBaselineExitUtc(new EntryUtc(from), nyTz, out var toExit))
                return false;

            DateTime to = toExit.Value;

            if (allMinutesSorted.Count == 0)
                return false;

            int start = LowerBound(allMinutesSorted, from);
            int end = LowerBound(allMinutesSorted, to);

            if (start >= end)
                return false;

            if (goLong)
            {
                double tp = entry * (1.0 + tpPct);
                double sl = slPct > 1e-9 ? entry * (1.0 - slPct) : double.NaN;

                for (int i = start; i < end; i++)
                {
                    var m = allMinutesSorted[i];

                    bool hitTp = m.High >= tp;
                    bool hitSl = !double.IsNaN(sl) && m.Low <= sl;
                    if (!hitTp && !hitSl) continue;

                    isSlDay = hitSl;
                    return true;
                }
            }
            else if (goShort)
            {
                double tp = entry * (1.0 - tpPct);
                double sl = slPct > 1e-9 ? entry * (1.0 + slPct) : double.NaN;

                for (int i = start; i < end; i++)
                {
                    var m = allMinutesSorted[i];

                    bool hitTp = m.Low <= tp;
                    bool hitSl = !double.IsNaN(sl) && m.High >= sl;
                    if (!hitTp && !hitSl) continue;

                    isSlDay = hitSl;
                    return true;
                }
            }

            return false;
        }

        private static int LowerBound(IReadOnlyList<Candle1m> xs, DateTime tUtc)
        {
            int lo = 0;
            int hi = xs.Count;

            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (xs[mid].OpenTimeUtc < tUtc) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        private static IReadOnlyList<BacktestRecord> BuildRecent(IReadOnlyList<BacktestRecord> ordered, int recentDays)
        {
            if (ordered.Count == 0)
                return Array.Empty<BacktestRecord>();

            var maxDateUtc = ordered[^1].EntryDayKeyUtc.Value;
            var fromRecentUtc = maxDateUtc.AddDays(-recentDays);

            var recent = ordered.Where(r => r.EntryDayKeyUtc.Value >= fromRecentUtc).ToList();
            return recent.Count == 0 ? ordered : recent;
        }

        private static BacktestModelStatsSegmentSnapshot? FindModelStatsSegment(
            BacktestModelStatsMultiSnapshot multi,
            ModelStatsSegmentKind kind)
        {
            if (multi == null) throw new ArgumentNullException(nameof(multi));
            return multi.Segments.FirstOrDefault(s => s.Kind == kind);
        }

        private static bool GetMovePredictionOrThrow(BacktestRecord r)
        {
            string reason = r.Reason ?? string.Empty;

            if (reason.StartsWith("day:move-", StringComparison.OrdinalIgnoreCase))
                return true;

            if (reason.StartsWith("day:flat", StringComparison.OrdinalIgnoreCase))
                return false;

            throw new InvalidOperationException(
                $"[diag-components] unexpected Reason='{reason}' for move model at {r.EntryUtc.Value:O}.");
        }

        private static bool? GetDirPredictionOrThrow(BacktestRecord r)
        {
            string reason = r.Reason ?? string.Empty;

            if (reason.Contains("move-up", StringComparison.OrdinalIgnoreCase))
                return true;

            if (reason.Contains("move-down", StringComparison.OrdinalIgnoreCase))
                return false;

            if (reason.StartsWith("day:flat", StringComparison.OrdinalIgnoreCase))
                return null;

            throw new InvalidOperationException(
                $"[diag-components] unexpected Reason='{reason}' for dir model at {r.EntryUtc.Value:O}.");
        }

        private static void AddMissing(Dictionary<string, int> dst, string? reason)
        {
            string key = string.IsNullOrWhiteSpace(reason) ? "<no-reason>" : reason;
            if (dst.TryGetValue(key, out int count))
                dst[key] = count + 1;
            else
                dst[key] = 1;
        }
    }
}
