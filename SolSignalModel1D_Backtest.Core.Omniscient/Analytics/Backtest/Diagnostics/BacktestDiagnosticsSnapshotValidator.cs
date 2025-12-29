namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Backtest.Diagnostics
{
    public static class BacktestDiagnosticsSnapshotValidator
    {
        public static IReadOnlyList<string> Validate(BacktestDiagnosticsSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var errors = new List<string>();

            if (snapshot.Coverage.RecordsTotal < snapshot.Coverage.TruthDailyLabelAvailable)
            {
                errors.Add(
                    $"[diag] truth_daily_label_available({snapshot.Coverage.TruthDailyLabelAvailable}) " +
                    $"exceeds records_total({snapshot.Coverage.RecordsTotal}).");
            }

            if (snapshot.Coverage.SlEvalBase > snapshot.Coverage.SlScoreAvailable)
            {
                errors.Add(
                    $"[diag] sl_eval_base({snapshot.Coverage.SlEvalBase}) > sl_score_available({snapshot.Coverage.SlScoreAvailable}).");
            }

            foreach (var seg in snapshot.Segments)
            {
                if (seg.Bases.NSlEval > seg.Bases.NSlScore)
                {
                    errors.Add(
                        $"[diag] segment '{seg.Label}': N_sl_eval({seg.Bases.NSlEval}) > N_sl_score({seg.Bases.NSlScore}).");
                }

                if (seg.Bases.NDailyEval > seg.RecordsCount)
                {
                    errors.Add(
                        $"[diag] segment '{seg.Label}': N_daily_eval({seg.Bases.NDailyEval}) > records({seg.RecordsCount}).");
                }
            }

            var full = snapshot.Segments.FirstOrDefault(s => s.Kind == BacktestDiagnosticsSegmentKind.Full);
            if (full != null)
            {
                var micro = snapshot.MicroStats.FlatOnly;
                if (micro.TotalFactDays != full.Bases.NMicroTruth)
                {
                    errors.Add(
                        $"[diag] micro base mismatch: micro.TotalFactDays({micro.TotalFactDays}) " +
                        $"!= full.NMicroTruth({full.Bases.NMicroTruth}).");
                }

                int microBase = micro.MicroUpPred + micro.MicroDownPred + micro.MicroNonePredicted;
                if (microBase != micro.TotalFactDays)
                {
                    errors.Add(
                        $"[diag] micro base sum mismatch: up+down+none({microBase}) != TotalFactDays({micro.TotalFactDays}).");
                }
            }

            return errors;
        }

        public static void ValidateOrThrow(BacktestDiagnosticsSnapshot snapshot)
        {
            var errors = Validate(snapshot);
            if (errors.Count == 0)
                return;

            throw new InvalidOperationException(
                "[diag] Diagnostics invariants failed: " + string.Join("; ", errors));
        }
    }
}
