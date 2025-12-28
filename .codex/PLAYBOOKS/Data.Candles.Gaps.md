# Playbook: свечи (candles), gap scan, инварианты рядов

## Когда открывать
- Любые задачи про загрузку/мердж свечей (6h/1h/1m), weekend-файл 1m, ресемплинг.
- Любые задачи про пропуски (gaps), дубли, пересечения, порядок рядов.
- Любые задачи про CLI `--scan-gaps-*`.

## Наблюдаемые элементы в оркестрации
- 6h: SOL/BTC/PAXG (`Ensure6hAvailable`, чтение, сортировка).
- 1h: SOL (чтение, сортировка).
- 1m: SOL из двух источников:
  - основной (weekdays),
  - отдельный weekend-файл: `SYMBOL-1m-weekends.ndjson`.

Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "LoadAllCandlesAndWindow|ReadAll6h|ReadAll1h|ReadAll1m\\b|ReadAll1mWeekends|CandlePaths\\.WeekendFile" .`

## Инварианты данных свечей
- 6h/1h/1m серии после init должны быть строго возрастающими по `OpenTimeUtc`.
- Любые дубли/пересечения по `OpenTimeUtc` — фатальная проблема (fail-fast).
- 1m weekday/weekend:
  - входные списки валидируются на строгую уникальность,
  - merge запрещает совпадения `OpenTimeUtc` между файлами,
  - итог строго возрастающий (доп. инвариант).

Поиск инвариантов:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "EnsureSortedAndStrictUnique1m|MergeSortedStrictUnique1m|EnsureStrictlyAscendingUtc|SortByKeyUtcInPlace" .`

## Gap scan (Binance klines)
- CLI флаги:
  - `--scan-gaps-1m`
  - `--scan-gaps-1h`
  - `--scan-gaps-6h`
- Канон сканера:
  - `BinanceKlinesGapScanner.ScanGapsAsync(...)` (`SolSignalModel1D_Backtest.Core.Data.Candles.Diagnostics`).

Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "scan-gaps-|BinanceKlinesGapScanner|ScanGapsAsync" .`

## Root-cause типовых «пропусков» (не симптомы)
- Некорректный merge weekday/weekend (пересечения или недогруз).
- NDJSON источник содержит дубли/пересортировку.
- Gap scan выявляет пропуск, который затем маскируется сортировкой/`OrderBy` позже в пайплайне.

## Мини-чеклист при отладке рядов
- Подтвердить, что сортировка выполняется один раз в init, а дальше только проверки.
- Подтвердить отсутствие `OrderBy` по 1m после init (если встречается — это подозрительно).
- Проверить границы времени: min/max `OpenTimeUtc` по каждому источнику и итоговому merge.
