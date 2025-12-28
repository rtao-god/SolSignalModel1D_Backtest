# Карта репозитория (высокий сигнал, низкий шум)

## Факты (по наблюдаемым неймспейсам/типам)
- `SolSignalModel1D_Backtest` — консольная оркестрация (partial `Program`), CLI флаги, пайплайн, отчёты.
- `SolSignalModel1D_Backtest.Core` — доменная логика: время/окна, каузальные данные, ML, backtest/PnL, аналитика.
- `SolSignalModel1D_Backtest.SanityChecks` — self-check/leakage проверки (`SelfCheckRunner`).
- `SolSignalModel1D_Backtest.Tests` — xUnit тесты.

## Стартовые точки поиска

### Оркестрация (Program partial)
Искать по символам:
- `Main(...)` (CLI флаги: `--scan-gaps-1m|1h|6h`)
- `BootstrapRowsAndCandlesAsync`
- `LoadAllCandlesAndWindow`
- `BuildIndicatorsAsync`
- `RunBacktestAndReports`
- `SplitByTrainUntilUtc`
- `DumpDailyAccuracyWithDatasetSplit`

Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "BootstrapRowsAndCandlesAsync|LoadAllCandlesAndWindow|BuildIndicatorsAsync|RunBacktestAndReports|SplitByTrainUntilUtc|DumpDailyAccuracyWithDatasetSplit" .`

### Время / NY-окна
- Канон: `NyWindowing` (namespace `SolSignalModel1D_Backtest.Core.Time`).
- Связанные типы (по именам): `EntryUtc`, `NyTradingEntryUtc`, `EntryDayKeyUtc`, `ExitDayKeyUtc`, `BaselineExitUtc`.
Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "NyWindowing|NyTradingEntryUtc|BaselineExit|EntryDayKeyUtc|ExitDayKeyUtc" .`

### Сплиты train/oos
- Канон split-helper: `NyTrainSplit` / `SplitByBaselineExitStrict(...)`.
- Файл: `NyTrainSplit.Strict.cs``

### Датасеты (Daily / SL)
- Daily: `SolSignalModel1D_Backtest.Core.Causal.ML.Daily` (`DailyDatasetBuilder.Build(...)`).
- SL: искать `Core.Causal.ML.SL` + `DatasetBuilder`/`OfflineBuilder`.
Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "Causal\\.ML\\.(Daily|SL)|DatasetBuilder|OfflineBuilder" .`

### Свечи / загрузка / хранение
- Candle IO: `CandleNdjsonStore`, `CandlePaths`, `CandleResampler`.
- Инварианты порядка: `SeriesGuards`.
- Weekend 1m: `CandlePaths.WeekendFile(symbol, "1m")`.
Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "CandleNdjsonStore|CandlePaths|CandleResampler|SeriesGuards|WeekendFile" .`

### Gap scan (Binance)
- `BinanceKlinesGapScanner.ScanGapsAsync(...)` + CLI флаги `--scan-gaps-*`.
Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "BinanceKlinesGapScanner|ScanGapsAsync|scan-gaps-" .`

### Индикаторы (FNG/DXY) и coverage
- `IndicatorsDailyUpdater.UpdateAllAsync(...)`, `EnsureCoverageOrFail(...)`, `FillMode`.
Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "IndicatorsDailyUpdater|EnsureCoverageOrFail|FillMode" .`

### Backtest / отчёты
- Runner: `BacktestRunner.Run(...)`.
- Orchestrator: `BacktestReportsOrchestrator.*`.
- Policies: `BacktestPolicyFactory`, `RollingLoop.PolicySpec`, `ICausalLeveragePolicy`.
Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "BacktestRunner|BacktestReportsOrchestrator|BacktestPolicyFactory|RollingLoop\\.PolicySpec|ICausalLeveragePolicy" .`

## Правило навигации при ошибке компиляции
1) взять символ из ошибки (`CS0103/CS1739/...`)
2) `rg` по символу
3) перейти к определению «каноничного» контракта (владелец)
4) привести call-sites к контракту (не наоборот)
