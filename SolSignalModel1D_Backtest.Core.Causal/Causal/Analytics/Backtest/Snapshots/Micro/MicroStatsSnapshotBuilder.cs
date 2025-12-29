using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;

namespace SolSignalModel1D_Backtest.Core.Causal.Causal.Analytics.Backtest.Snapshots.Micro
{
    /// <summary>
    /// Микро-статистика:
    /// 1) По всем дням с валидным micro-фактом (MicroTruth), независимо от PredLabel_Day.
    /// 2) Направленная точность по дням, где и pred, и truth в {0,2} (direction определена).
    /// </summary>
    public static class MicroStatsSnapshotBuilder
    {
        public static MicroStatsSnapshot Build(IReadOnlyList<BacktestAggRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (rows.Count == 0)
                throw new InvalidOperationException("[micro-stats] rows=0: невозможно построить микростатистику без данных.");

            var flatOnly = BuildFlatOnly(rows);
            var nonFlat = BuildNonFlatDirection(rows);

            return new MicroStatsSnapshot
            {
                FlatOnly = flatOnly,
                NonFlatDirection = nonFlat
            };
        }

        private static FlatOnlyMicroBlock BuildFlatOnly(IReadOnlyList<BacktestAggRow> rows)
        {
            int totalFactDays = 0;
            int microUpPred = 0, microUpHit = 0, microUpMiss = 0;
            int microDownPred = 0, microDownHit = 0, microDownMiss = 0;
            int microNone = 0;

            foreach (var r in rows)
            {
                if (r.PredMicroUp && r.PredMicroDown)
                    throw new InvalidOperationException($"[micro-stats] Both PredMicroUp/PredMicroDown are true for {r.EntryDayKeyUtc.Value:O}.");


                if (!r.MicroTruth.HasValue)
                    continue;

                totalFactDays++;

                bool truthUp = r.MicroTruth.Value == MicroTruthDirection.Up;
                bool anyPred = false;

                if (r.PredMicroUp)
                {
                    anyPred = true;
                    microUpPred++;
                    if (truthUp) microUpHit++; else microUpMiss++;
                }

                if (r.PredMicroDown)
                {
                    anyPred = true;
                    microDownPred++;
                    if (!truthUp) microDownHit++; else microDownMiss++;
                }

                if (!anyPred)
                    microNone++;
            }

            int totalDirPred = microUpPred + microDownPred;
            int totalDirHit = microUpHit + microDownHit;
            var coverage = totalFactDays > 0
                ? OptionalValue<double>.Present((double)totalDirPred / totalFactDays * 100.0)
                : OptionalValue<double>.Missing(MissingReasonCodes.MicroNoTruth);

            var accUp = microUpPred > 0
                ? OptionalValue<double>.Present((double)microUpHit / microUpPred * 100.0)
                : OptionalValue<double>.Missing(MissingReasonCodes.MicroNoUpPred);

            var accDown = microDownPred > 0
                ? OptionalValue<double>.Present((double)microDownHit / microDownPred * 100.0)
                : OptionalValue<double>.Missing(MissingReasonCodes.MicroNoDownPred);

            var accAll = totalDirPred > 0
                ? OptionalValue<double>.Present((double)totalDirHit / totalDirPred * 100.0)
                : OptionalValue<double>.Missing(MissingReasonCodes.MicroNoPredictions);

            var accAllWithNone = totalFactDays > 0
                ? OptionalValue<double>.Present((double)totalDirHit / totalFactDays * 100.0)
                : OptionalValue<double>.Missing(MissingReasonCodes.MicroNoTruth);

            return new FlatOnlyMicroBlock
            {
                TotalFactDays = totalFactDays,
                MicroUpPred = microUpPred,
                MicroUpHit = microUpHit,
                MicroUpMiss = microUpMiss,
                MicroDownPred = microDownPred,
                MicroDownHit = microDownHit,
                MicroDownMiss = microDownMiss,
                MicroNonePredicted = microNone,
                TotalDirPred = totalDirPred,
                TotalDirHit = totalDirHit,
                CoveragePct = coverage,
                AccUpPct = accUp,
                AccDownPct = accDown,
                AccAllPct = accAll,
                AccAllWithNonePct = accAllWithNone
            };
        }

        private static NonFlatDirectionBlock BuildNonFlatDirection(IReadOnlyList<BacktestAggRow> rows)
        {
            var data = rows
                .Where(r => (r.PredLabel_Day == 0 || r.PredLabel_Day == 2) && (r.TrueLabel == 0 || r.TrueLabel == 2))
                .ToList();

            int total = data.Count;
            int correct = data.Count(r => r.TrueLabel == r.PredLabel_Day);

            if (total == 0)
                throw new InvalidOperationException("[micro-stats] non-flat direction has no data.");

            int predUp_factUp = data.Count(r => r.PredLabel_Day == 2 && r.TrueLabel == 2);
            int predUp_factDown = data.Count(r => r.PredLabel_Day == 2 && r.TrueLabel == 0);
            int predDown_factDown = data.Count(r => r.PredLabel_Day == 0 && r.TrueLabel == 0);
            int predDown_factUp = data.Count(r => r.PredLabel_Day == 0 && r.TrueLabel == 2);
            var acc = total > 0
                ? OptionalValue<double>.Present((double)correct / total * 100.0)
                : OptionalValue<double>.Missing(MissingReasonCodes.MicroNoNonFlatDirection);

            return new NonFlatDirectionBlock
            {
                Total = total,
                Correct = correct,
                PredUp_FactUp = predUp_factUp,
                PredUp_FactDown = predUp_factDown,
                PredDown_FactDown = predDown_factDown,
                PredDown_FactUp = predDown_factUp,
                AccuracyPct = acc
            };
        }
    }
}
