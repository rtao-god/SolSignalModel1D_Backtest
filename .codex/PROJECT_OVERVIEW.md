# Обзор проекта (для ориентации Codex)

## Факты
- Репозиторий — C#/.NET решение с консольной оркестрацией (partial `Program`) и несколькими проектами (Core / Tests / SanityChecks / Api для фронта).
- В коде встречаются неймспейсы `SolSignalModel1D_Backtest.Core.*`, включая `Core.Causal.*` и `Core.Omniscient.*`.
- ML часть использует ML.NET и LightGBM.
- В runtime-пайплайне есть:
  - загрузка свечей 6h/1h/1m (1m включает weekday + отдельный weekend-файл),
  - gap scan по Binance klines (`--scan-gaps-*`),
  - обновление дневных индикаторов (FNG/DXY) и проверка coverage (fail-fast),
  - train/oos split по baseline-exit day-key.

## Предположения 
- Основной пайплайн: данные (candles/indicators) → построение каузальных дневных строк → обучение/инференс → построение omniscient `BacktestRecord` → backtest/PnL/отчёты → sanity checks.
- Целевая платформа сборки близка к `net9.0`.

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
Ключевые типы:
- `EntryUtc`: UTC instant момента входа.
- `NyTradingEntryUtc`: валидированный вход «NY morning торгового дня».
- `EntryDayKeyUtc` / `ExitDayKeyUtc`: day-key (00:00Z) с явной семантикой.
- `BaselineExitUtc`: момент baseline-exit (следующее NY-утро минус 2 минуты; Friday → Monday).

Каноничный владелец правил NY-времени:
- `NyWindowing` — единственный источник истины по NY morning / baseline-exit / day-key derivations.

## Семантика train/oos split
- Сплит должен быть основан на **day-key baseline-exit**, а не на «наивных датах» entry.
- Excluded должны существовать и быть осмысленно обработаны:
  - мягкий сплит: учитывать excluded,
  - строгий сплит: excluded запрещён (fail-fast).

## Данные свечей (высокоуровнево)
- 6h: SOL/BTC/PAXG.
- 1h: SOL.
- 1m: SOL из двух источников (weekdays + weekend-файл).
- Инвариант: серии после init строго возрастающие и строго уникальные по времени; weekday/weekend 1m не пересекаются.

## Индикаторы (FNG/DXY)
- Обновление идёт на интервале `[fromUtc..toUtc]`.
- Coverage должен валидироваться до продолжения пайплайна (fail-fast).
- FillMode: FNG = Strict, DXY = NeutralFill.

## Что оптимизировать в правках
- Строгая типобезопасность времени (без смешивания смыслов).
- Отсутствие leakage и соблюдение causal ≠ omniscient.
- Минимальные диффы: править call-sites под каноничные контракты.
- Детерминизм и fail-fast при невозможных состояниях.
