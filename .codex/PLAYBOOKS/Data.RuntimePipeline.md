# Данные, окна NY и сплиты: рантайм-пайплайн (runbook)

## Назначение
Карта местности для задач, затрагивающих:
- загрузку/валидацию свечей (6h/1h/1m + weekend-файл),
- gap scan (Binance),
- индикаторы (FNG/DXY) и coverage,
- NY-окна (NY morning, baseline-exit = NY morning - 2m, Friday→Monday),
- train/oos split по **baseline-exit day-key**, а не по entry-day.

## Термины (минимум)
- **EntryUtc**: UTC instant момента входа.
- **NY morning**: локальное NY-утро (07:00 зимой / 08:00 летом; DST).
- **BaselineExitUtc**: *следующее* NY-утро **минус 2 минуты**.
  - Friday entry: baseline-exit переносится на **понедельник** (addDays=3).
- **Day-key (00:00Z)**: ключ суток, не instant.
  - Для сплитов используется **day-key baseline-exit**, а не entry-day.

## 1) Свечи: загрузка, сортировка, инварианты

### 1.1 Источники свечей
- 6h: SOL/BTC/PAXG
- 1h: SOL
- 1m: SOL двумя файлами:
  - основной (weekdays),
  - отдельный weekend-файл (`SYMBOL-1m-weekends.ndjson`;

Ключевые символы для поиска:
- `LoadAllCandlesAndWindow`
- `CandleResampler.Ensure6hAvailable`
- `ReadAll6h`, `ReadAll1h`, `ReadAll1m`, `ReadAll1mWeekends`
- `CandlePaths.WeekendFile`
- `CandleNdjsonStore`
- `SeriesGuards.SortByKeyUtcInPlace`, `SeriesGuards.EnsureStrictlyAscendingUtc`
- `EnsureSortedAndStrictUnique1m`, `MergeSortedStrictUnique1m`

Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "LoadAllCandlesAndWindow|CandleResampler\\.Ensure6hAvailable|ReadAll6h|ReadAll1h|ReadAll1m\\b|ReadAll1mWeekends|CandlePaths\\.WeekendFile|CandleNdjsonStore|SeriesGuards|EnsureSortedAndStrictUnique1m|MergeSortedStrictUnique1m" .`

### 1.2 Инварианты порядка/уникальности (1m — самый строгий)
- `EnsureSortedAndStrictUnique1m(xs, tag)`:
  - допускает один локальный sort, если порядок нарушен,
  - затем валидирует строгую уникальность: `cur <= prev` → throw.
- `MergeSortedStrictUnique1m(weekdays, weekends)`:
  - входные списки уже строго возрастающие,
  - любое совпадение `OpenTimeUtc` между weekday/weekend → throw,
  - итог также строго возрастающий.

Практическая интерпретация:
- weekend-файл не имеет права пересекаться по минутам с основным 1m;
- любые дубли/пересечения — фатальная проблема данных, не «починим сортировкой».

### 1.3 Окно данных (fromUtc/toUtc) и нормализация дат
- `toUtc` вычисляется от последней 6h-свечи SOL:
  - `lastUtc = solAll6h[^1].OpenTimeUtc`
  - `toUtc = lastUtc.ToCausalDateUtc()`
- `fromUtc = FullBackfillFromUtc.ToCausalDateUtc()`

Важно:
- `ToCausalDateUtc()` по имени выглядит как нормализация к day-key (00:00Z), но это нужно подтвердить по реализации.

Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "\\bToCausalDateUtc\\b|FullBackfillFromUtc" .`

## 2) Gap scan (Binance)
Оркестрация:
- CLI флаги:
  - `--scan-gaps-1m`, `--scan-gaps-1h`, `--scan-gaps-6h`
- Канон:
  - `BinanceKlinesGapScanner.ScanGapsAsync(http, symbol, interval, tf, fromUtc, toUtc)`

Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "scan-gaps-|BinanceKlinesGapScanner|ScanGapsAsync" .`

## 3) Индикаторы (FNG/DXY) и coverage gate
Наблюдаемое:
- `IndicatorsDailyUpdater.UpdateAllAsync(rangeStartUtc, rangeEndUtc, fngFillMode: Strict, dxyFillMode: NeutralFill)`
- затем `EnsureCoverageOrFail(fromUtc, toUtc)` как fail-fast gate.

Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "IndicatorsDailyUpdater|UpdateAllAsync|EnsureCoverageOrFail|fngFillMode|dxyFillMode|FillMode" .`

Инвариант:
- пайплайн не должен продолжаться на дырявых/битых рядах индикаторов.

## 4) NY-окна и baseline-exit (-2 минуты)
Канон:
- `NyWindowing` (DST, NY morning, baseline-exit, weekend eligibility).

Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "NyWindowing|BaselineExit|NyTradingEntryUtc" .`

Инварианты:
- baseline-exit = следующее NY-утро - 2 минуты;
- Friday entry → baseline-exit на понедельник;
- в causal weekend запрещён (Try*→false, OrThrow→throw).

## 5) Train/OOS split по baseline-exit day-key
Наблюдаемое из оркестрации:
- `SplitByTrainUntilUtc(records, trainUntilUtc, ...)`:
  - вычисляет `trainUntilExitDayKeyUtc = ExitDayKeyUtc.FromUtcMomentOrThrow(trainUntilUtc)`
  - вызывает `NyTrainSplit.SplitByBaselineExitStrict(ordered, entrySelector, trainUntilExitDayKeyUtc, nyTz, tag)`

Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "SplitByTrainUntilUtc|ExitDayKeyUtc\\.FromUtcMomentOrThrow|NyTrainSplit\\.SplitByBaselineExitStrict" .`

Инварианты:
- сортировка записей для split’а должна быть по `EntryUtc.Value`, а не по «каузальным DateUtc»/day-key;
- `trainUntilUtc` должен быть `DateTimeKind.Utc` и non-default (fail-fast);
- `tag` обязателен и должен быть стабильным для диагностики.

## 6) Bootstrap и строгие проверки монотонности
Наблюдаемое:
- `BootstrapRowsAndCandlesAsync()` делает `SeriesGuards.EnsureStrictlyAscendingUtc(...)` для:
  - свечей 6h/1h/1m (`OpenTimeUtc`)
  - дневных строк (`r.Causal.EntryUtc.Value`) и morning subset

Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "BootstrapRowsAndCandlesAsync|EnsureStrictlyAscendingUtc\\(" .`
