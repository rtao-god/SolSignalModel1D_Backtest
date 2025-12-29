using SolSignalModel1D_Backtest.Core.Causal.ML.Utils;
using SolSignalModel1D_Backtest.Core.Causal.Utils;

namespace SolSignalModel1D_Backtest.Core.Causal.Causal.ML.Daily
{
    public static class DailyTrainingDataBuilder
    {
        public static void Build(
            IReadOnlyList<LabeledCausalRow> trainRows,
            bool balanceMove,
            bool balanceDir,
            double balanceTargetFrac,
            out List<LabeledCausalRow> moveTrainRows,
            out List<LabeledCausalRow> dirNormalRows,
            out List<LabeledCausalRow> dirDownRows)
        {
            if (trainRows == null) throw new ArgumentNullException(nameof(trainRows));
            if (trainRows.Count == 0)
                throw new InvalidOperationException("[daily-train] trainRows is empty.");

            // Инвариант: вход отсортирован по entryUtc (UTC) строго по возрастанию.
            // EntryDayKeyUtc может повторяться внутри одного дня (intraday ряды).
            SeriesGuards.EnsureStrictlyAscendingUtc(
                trainRows,
                r => r.EntryUtc.Value,
                "daily-train.trainRows");

            // ===== 1) Move: все дни =====
            if (balanceMove)
            {
                moveTrainRows = MlTrainingUtils.OversampleBinary(
                    src: trainRows,
                    isPositive: r => r.TrueLabel != 1,
                    dateSelector: r => r.EntryDayKeyUtc.Value,
                    targetFrac: balanceTargetFrac);
            }
            else
            {
                moveTrainRows = trainRows as List<LabeledCausalRow> ?? trainRows.ToList();
            }

            // ===== 2) Dir: только не-flat дни (up/down) =====
            var moveRows = trainRows
                .Where(r => r.TrueLabel == 0 || r.TrueLabel == 2)
                .ToList();

            dirNormalRows = moveRows
                .Where(r => !r.Causal.RegimeDown)
                .ToList();

            dirDownRows = moveRows
                .Where(r => r.Causal.RegimeDown)
                .ToList();

            if (balanceDir)
            {
                dirNormalRows = MlTrainingUtils.OversampleBinary(
                    src: dirNormalRows,
                    isPositive: r => r.TrueLabel == 2,
                    dateSelector: r => r.EntryDayKeyUtc.Value,
                    targetFrac: balanceTargetFrac);

                dirDownRows = MlTrainingUtils.OversampleBinary(
                    src: dirDownRows,
                    isPositive: r => r.TrueLabel == 2,
                    dateSelector: r => r.EntryDayKeyUtc.Value,
                    targetFrac: balanceTargetFrac);
            }
        }
    }
}
