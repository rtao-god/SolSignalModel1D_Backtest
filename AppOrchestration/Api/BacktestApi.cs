using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Causal.Time;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest
{
    /// <summary>
    /// Частичный класс Program: публичный энтрипоинт для сборки снапшота бэктеста,
    /// переиспользуемый как консолью, так и API.
    /// </summary>
    public partial class Program
    {
        /// <summary>
        /// Высокоуровневый пайплайн подготовки данных для бэктеста/превью.
        ///
        /// Логика по слоям:
        /// 1) BootstrapDataAsync — общий инфраструктурный бутстрап:
        ///    - обновление свечей;
        ///    - загрузка всех временных рядов;
        ///    - индикаторы;
        ///    - дневные строки (DailyRowsBundle).
        /// 2) Поверх бутстрапа:
        ///    - дневная модель (PredictionRecord);
        ///    - SL-модель;
        ///    - Delayed A по минуткам.
        ///
        /// На выходе выдаётся стабильный контракт BacktestDataSnapshot,
        /// который использует BacktestEngine и API-превью.
        /// </summary>
        public static async Task<BacktestDataSnapshot> BuildBacktestDataAsync()
        {
            // 1. Общий бутстрап данных (свечи + индикаторы + дневные строки).
            var bootstrap = await BootstrapDataAsync();

            var rowsBundle = bootstrap.RowsBundle;
            var allRows = rowsBundle.AllRows;
            var mornings = rowsBundle.Mornings;

            Console.WriteLine($"[rows] mornings (NY window) = {mornings.Count}");
            if (mornings.Count == 0)
                throw new InvalidOperationException("[rows] После фильтров нет утренних точек.");

            // 2. Основная дневная модель: строим prediction-записи по утренним точкам.
            List<BacktestRecord> records;
            {
                var engine = CreatePredictionEngineOrFallback(allRows);

                records = await LoadPredictionRecordsAsync(
                    mornings,
                    bootstrap.Sol1m,
                    engine
                );

                Console.WriteLine($"[records] built = {records.Count}");
            }

            // 3. SL-модель: обучаемся строго на TrainOnly<BacktestRecord> по baseline-exit контракту.
            {
                var trainUntilExitDayKeyUtc = _trainUntilExitDayKeyUtc;

                var orderedRecords = records
                    .OrderBy(r => r.Causal.EntryUtc.Value)
                    .ToList();

                Console.WriteLine(
                    $"[sl] запуск SplitByBaselineExitStrict: тег='sl', trainUntilExitDayKeyUtc={trainUntilExitDayKeyUtc.Value:yyyy-MM-dd}");

                var recSplit = NyTrainSplit.SplitByBaselineExitStrict(
                    ordered: orderedRecords,
                    entrySelector: static r => r.Causal.EntryUtc,
                    trainUntilExitDayKeyUtc: trainUntilExitDayKeyUtc,
                    nyTz: NyTz,
                    tag: "sl");

                Console.WriteLine(
                    $"[sl] records split: train={recSplit.Train.Count}, oos={recSplit.Oos.Count}, " +
                    $"trainUntilExitDayKey={NyTrainSplit.ToIsoDate(trainUntilExitDayKeyUtc)}");

                if (recSplit.Train.Count < 50)
                {
                    var trMin = recSplit.Train.Count > 0 ? recSplit.Train.Min(r => r.Causal.EntryDayKeyUtc.Value) : default;
                    var trMax = recSplit.Train.Count > 0 ? recSplit.Train.Max(r => r.Causal.EntryDayKeyUtc.Value) : default;

                    throw new InvalidOperationException(
                        $"[sl] SL train subset too small (count={recSplit.Train.Count}). " +
                        $"period={(recSplit.Train.Count > 0 ? $"{trMin:yyyy-MM-dd}..{trMax:yyyy-MM-dd}" : "n/a")}.");
                }

                TrainAndApplySlModelOffline(
                    trainRecords: recSplit.Train,
                    records: records,
                    sol1h: bootstrap.SolAll1h,
                    sol1m: bootstrap.Sol1m,
                    solAll6h: bootstrap.SolAll6h
                );
            }

            // 4. Delayed A: расчёт отложенной доходности по минутным свечам.
            {
                PopulateDelayedA(
                    records: records,
                    allRows: allRows,
                    trainUntilExitDayKeyUtc: _trainUntilExitDayKeyUtc,
                    sol1h: bootstrap.SolAll1h,
                    solAll6h: bootstrap.SolAll6h,
                    sol1m: bootstrap.Sol1m,
                    dipFrac: 0.005,
                    tpPct: 0.010,
                    slPct: 0.010
                );
            }

            // 5. Финальный снэпшот.
            return new BacktestDataSnapshot
            {
                Mornings = mornings,
                Records = records,
                Candles1m = bootstrap.Sol1m
            };
        }
    }
}

