# Playbook: NY-окна (NyWindowing)

## Когда открывать
- Любые задачи про: NY morning, baseline-exit, weekend eligibility, DST, day-key derivations.
- Любые задачи про train/oos split, если на границе фигурируют пятницы/понедельники.

## Каноничный владелец
- `NyWindowing` (namespace `SolSignalModel1D_Backtest.Core.Time`).

## Термины и инварианты
- Входы времени — **UTC instant** (не day-key), и `DateTimeKind` должен быть `Utc`.
- **NY morning**: локальное NY-утро (07:00 зимой / 08:00 летом; зависит от DST).
- **BaselineExitUtc**: *следующее* NY-утро **минус 2 минуты**.
- **Friday entry**: baseline-exit переносится на **понедельник** (NY-логика).
- Weekend eligibility определяется по NY локальному времени:
  - в causal-компонентах weekend запрещён: `Try*` возвращает `false`, `OrThrow` кидает исключение.

## Типовые точки использования
Искать по символам:
- `BaselineExitOrThrow(...)`
- `TryBaselineExit(...)`
- `NextNyMorningUtcOrThrow(...)`
- `NyTradingEntryUtc` (валидация входа)
- `EntryDayKeyUtc` / `ExitDayKeyUtc` derivations

Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "BaselineExit|NyTradingEntryUtc|NextNyMorning|EntryDayKeyUtc|ExitDayKeyUtc" .`

## Частые ошибки (корень)
- Сравнение `TrainUntilUtc` с `EntryUtc` вместо `BaselineExitUtc`.
- Использование day-key как instant (или наоборот).
- «Лечение» weekend исключением из данных без отражения в split/excluded (утечка смысла).

## Мини-чеклист при правке
- Любая функция, принимающая `DateTime`, должна проверять `DateTimeKind.Utc` (или принимать семантический тип).
- Любой split/граница должен использовать один канон вычисления порога (например, `ExitDayKeyUtc.FromUtcMomentOrThrow(trainUntilUtc)`).
- Любое исключение/ошибка на границе времени должно включать: `entryUtc`, NY-local stamp, baseline-exit, `tag`.
