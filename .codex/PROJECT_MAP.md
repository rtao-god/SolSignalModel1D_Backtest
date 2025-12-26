# Карта репозитория (высокий сигнал, низкий шум)

## Факты (по наблюдаемым путям/неймспейсам из кода)
- `SolSignalModel1D_Backtest` — консольная оркестрация (Program.cs).
- `SolSignalModel1D_Backtest.Core` — доменная логика, время, ML, бэктест, аналитика.
- `SolSignalModel1D_Backtest.SanityChecks` — self-check/leakage проверки.
- `SolSignalModel1D_Backtest.Tests` — xUnit тесты.

## Стартовые точки поиска (подтверждать наличием файлов в репо)
### Время / NY-окна
- `SolSignalModel1D_Backtest.Core/Time/NyWindowing*.cs`
  - NY morning validation
  - baseline-exit computation
  - derivations day-key (entry/exit)

### Сплиты / датасеты
- `SolSignalModel1D_Backtest.Core/Causal/ML/Daily/*DatasetBuilder*.cs`
- `SolSignalModel1D_Backtest.Core/Causal/ML/SL/*DatasetBuilder*.cs`
- split-helpers обычно рядом с time-моделью (`Core/Time` или `Core/Causal/Time`) — искать по символам `NyTrainSplit`, `SplitByBaselineExit*`, `ClassifyByBaselineExit`.

### Бэктест и аналитика
- `SolSignalModel1D_Backtest.Core/Backtest/BacktestRunner.cs`
- `SolSignalModel1D_Backtest.Core/*/Analytics/*/Snapshots/*.cs`
- `SolSignalModel1D_Backtest.Core/*/Analytics/*/Printers/*.cs`

### SL offline builder
- `SolSignalModel1D_Backtest.Core/ML/SL/SlOfflineBuilder.cs`

### Sanity/leakage
- `SolSignalModel1D_Backtest.SanityChecks/**`

### Тесты
- `SolSignalModel1D_Backtest.Tests/**`
  - при миграциях времени: обновлять тесты на каноничные фабрики (например, NyWindowing factory methods), без обхода obsolete API.

## Правило навигации при ошибке компиляции
1) взять символ из ошибки (`CS0103/CS1739/...`)
2) `rg` по символу
3) перейти к определению «каноничного» контракта (владелец)
4) привести call-sites к контракту (не наоборот)
