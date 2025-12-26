# Данные, окна NY и сплиты: рантайм-пайплайн (runbook)

Цель: дать Codex «карту местности» для задач, затрагивающих:
- загрузку/валидацию свечей (6h/1h/1m + weekend-файл),
- gap scan (Binance),
- индикаторы (FNG/DXY) и coverage,
- NY-окна (NY morning, baseline-exit = NY morning - 2m, Friday→Monday),
- train/oos split по **baseline-exit day-key**, а не по entry-day.

> Важно: этот файл — про доменную семантику и «где искать».  
> Конкретные пути файлов/классов всегда подтверждать `rg`/Go to Definition.

---

## Термины (минимум)

- **EntryUtc**: UTC instant момента входа.
- **NY morning**: локальное NY-утро (07:00 зимой / 08:00 летом; DST).
- **BaselineExitUtc**: *следующее* NY-утро **минус 2 минуты**.
  - Friday entry: baseline-exit переносится на **понедельник** (addDays=3).
- **Day-key (00:00Z)**: ключ суток, не instant.
  - Для сплитов используется **day-key baseline-exit**, а не entry-day.

Где владелец семантики NY:
- `NyWindowing` (каноничный источник истины: DST, weekend-eligibility, baseline-exit, day-key derivations).
  - Найти: `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "\bNyWindowing\b" .`

---

## 1) Свечи: загрузка, сортировка, инварианты

### 1.1. Источники данных свечей (наблюдаемое из Program)
- 6h: SOL/BTC/PAXG
- 1h: SOL
- 1m: SOL **двумя файлами**:
  - основной (weekdays),
  - отдельный weekend-файл (`SYMBOL-1m-weekends.ndjson`).

Ключевые функции/символы (искать `rg`):
- `LoadAllCandlesAndWindow`
- `CandleResampler.Ensure6hAvailable(...)`
- `ReadAll6h / ReadAll1h / ReadAll1m / ReadAll1mWeekends`
- `CandlePaths.WeekendFile(symbol, "1m")`
- `CandleNdjsonStore`
- `SeriesGuards.SortByKeyUtcInPlace(...)`
- `SeriesGuards.EnsureStrictlyAscendingUtc(...)`

Команды поиска:
```bash
rg -n --glob "!**/bin/**" --glob "!**/obj/**" "\bLoadAllCandlesAndWindow\b" .
rg -n --glob "!**/bin/**" --glob "!**/obj/**" "\bEnsureSortedAndStrictUnique1m\b|\bMergeSortedStrictUnique1m\b" .
rg -n --glob "!**/bin/**" --glob "!**/obj/**" "\bCandleResampler\.Ensure6hAvailable\b|\bCandleNdjsonStore\b|\bCandlePaths\.WeekendFile\b" .
```

### 1.2. Инварианты порядка/уникальности (1m — самый строгий)

- Из текущего кода (Program partial):

EnsureSortedAndStrictUnique1m(xs, tag):

допускает один локальный sort, если порядок нарушен,

затем валидирует строгую уникальность: cur <= prev → throw.

MergeSortedStrictUnique1m(weekdays, weekends):

входные списки уже строго возрастающие,

любое совпадение OpenTimeUtc между weekday/weekend → throw,

итог также строго возрастающий (доп. инвариант с lastTime).

Практическая интерпретация:

weekend-файл не имеет права пересекаться по минутам с основным 1m.

любые дубли/пересечения — фатальная проблема данных, не «починим сортировкой».

1.3. Окно данных (fromUtc/toUtc) и нормализация дат

Наблюдаемое:

toUtc вычисляется от последней 6h-свечи SOL:

lastUtc = solAll6h[^1].OpenTimeUtc;

toUtc = lastUtc.ToCausalDateUtc();

fromUtc = FullBackfillFromUtc.ToCausalDateUtc();

Важно:

ToCausalDateUtc() выглядит как «нормализация к day-key» (00:00Z), но это надо подтвердить по определению метода.

Найти: rg -n "\bToCausalDateUtc\b" . и открыть реализацию.