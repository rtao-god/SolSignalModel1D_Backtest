# Обзор проекта (для ориентации Codex)

## Факты
- Репозиторий — C#/.NET решение с консольной оркестрацией (Program.cs) и несколькими проектами (Core / Tests / SanityChecks).
- В коде встречаются неймспейсы вида `SolSignalModel1D_Backtest.Core.*`, включая `Core.Causal.*` и `Core.Omniscient.*`.
- ML часть использует ML.NET и LightGBM (по using/типам тренеров).

## Предположения (требуют подтверждения поиском по репо)
- Основной пайплайн: построение каузальных рядов → обучение моделей → построение omniscient backtest records → аналитика/снапшоты/принтеры → sanity checks.
- Целевая платформа сборки близка к net9.0 (встречалось в путях/артефактах сборки), но нужно подтверждать в csproj.

## Граница домена: causal vs omniscient
### Causal слой (не должен «подглядывать»)
- Обычно: `Core.Causal.*`
- Работает с каузальными входами (например: `CausalDataRow` / `LabeledCausalRow`).
- Запрет: любые признаки/лейблы, зависящие от будущего.

### Omniscient слой (для оценки/бэктеста)
- Обычно: `Core.Omniscient.*`
- Содержит `BacktestRecord` и forward outcomes.
- Используется для оценки качества, отчётов, симуляции PnL, диагностик.

## Модель времени (семантические типы)
Ключевые типы (по именам, подтверждать переходом к определению):
- `EntryUtc`: UTC instant момента входа.
- `NyTradingEntryUtc`: валидированный вход «NY morning торгового дня».
- `EntryDayKeyUtc` / `ExitDayKeyUtc`: day-key (00:00Z) с явной семантикой.
- `BaselineExitUtc`: момент baseline-exit (следующее NY-утро минус 2 минуты; Friday → Monday).

Каноничный владелец правил NY-времени:
- `NyWindowing` — единственный источник истины по NY morning / baseline-exit / day-key derivations.

## Семантика train/oos split
- Сплит должен быть основан на day-key baseline-exit, а не на «наивных датах».
- Excluded должны существовать и быть осмысленно обработаны:
  - мягкий сплит: учитывать excluded,
  - строгий сплит: excluded запрещён (fail-fast).

## ML компоненты (высокоуровнево)
- `ModelTrainer` обучает набор бинарных классификаторов:
  - move (move vs flat),
  - dir (normal vs down-regime),
  - micro-flat (в flat-дни micro up vs down).
- SL подсистема (по именам классов):
  - offline builder строит сэмплы по 1m path labels + 1h features,
  - dataset builder фильтрует/готовит датасет по train boundary,
  - overlay применяет риск-слой к итоговым вероятностям.

## Что оптимизировать в правках
- Строгая типобезопасность времени (без смешивания смыслов).
- Отсутствие leakage и соблюдение causal ≠ omniscient.
- Минимальные диффы: править call-sites под каноничные контракты.
- Детерминизм и fail-fast при невозможных состояниях.
