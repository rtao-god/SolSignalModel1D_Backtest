using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Daily;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Micro;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.Rows;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.SL;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Pnl;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks
{
    /// <summary>
    /// Единая точка входа для self-check'ов.
    /// Никаких зависимостей от консоли/UI — только расчёты и флаги.
    /// </summary>
    public static class SelfCheckRunner
    {
        /// <summary>
        /// Запускает набор sanity-проверок на уже собранных артефактах пайплайна.
        /// </summary>
        public static Task<SelfCheckResult> RunAsync(
            SelfCheckContext ctx,
            CancellationToken cancellationToken = default)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.NyTz == null) throw new ArgumentNullException(nameof(ctx.NyTz));
            if (ctx.TrainUntilExitDayKeyUtc.IsDefault)
                throw new ArgumentException("ctx.TrainUntilExitDayKeyUtc must be initialized (non-default).", nameof(ctx));
            if (ctx.Records != null && ctx.Records.Count > 0 && ctx.NyTz.Id == TimeZoneInfo.Utc.Id)
                throw new InvalidOperationException("[self-check] NyTz is UTC for non-empty records. Set NY timezone explicitly.");

            var results = new List<SelfCheckResult>();

            // =====================================================================
            // 1) Daily: train/OOS + shuffle.
            // =====================================================================
            if (ctx.Records != null && ctx.Records.Count > 0)
            {
                results.Add(
                    DailyLeakageChecks.CheckDailyTrainVsOosAndShuffle(
                        ctx.Records,
                        ctx.TrainUntilExitDayKeyUtc,
                        ctx.NyTz,
                        ctx.AllRows));

                // Доп. диагностика (лог в консоль): bare-PnL + shuffle.
                DailyBarePnlChecks.LogDailyBarePnlWithBaselinesAndShuffle(
                    ctx.Records,
                    ctx.TrainUntilExitDayKeyUtc,
                    ctx.NyTz,
                    shuffleRuns: 20);
            }
            else
            {
                results.Add(SelfCheckResult.Ok("[daily] records отсутствуют — дневные проверки пропущены."));
            }

            // =====================================================================
            // 2) Micro-layer
            // =====================================================================
            if (ctx.Mornings != null
                && ctx.Mornings.Count > 0
                && ctx.Sol1m != null
                && ctx.Sol1m.Count > 0)
            {
                results.Add(MicroLeakageChecks.CheckMicroLayer(ctx));
            }

            // =====================================================================
            // 3) SL-layer
            // =====================================================================
            if (ctx.Records != null
                && ctx.Records.Count > 0
                && ctx.SolAll6h != null
                && ctx.SolAll6h.Count > 0
                && ctx.SolAll1h != null
                && ctx.SolAll1h.Count > 0)
            {
                results.Add(SlLeakageChecks.CheckSlLayer(ctx));
            }

            // =====================================================================
            // 4) Row features vs future-fields
            // =====================================================================
            results.Add(RowFeatureLeakageChecks.CheckRowFeaturesAgainstFuture(ctx));

            // =====================================================================
            // 5) Aggregation
            // =====================================================================
            var aggregate = SelfCheckResult.Aggregate(results);
            return Task.FromResult(aggregate);
        }
    }
}
