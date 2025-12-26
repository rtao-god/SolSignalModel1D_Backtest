# AGENTS

## Язык и стиль
- Всё (ответы, сообщения, комментарии в коде) — только на русском.
- Пиши технически и по делу, без лишних пояснений и «воды».

## Правдивость (обязательно)
- Явно помечай: **Факты / Предположения / Гипотезы**.
- Не выдумывай контекст/контракты/пути/имена. Если что-то не проверял (сборка/тесты/рантайм) — так и напиши.

## Скоуп и совместимость
- Делай только то, что я попросил. Без «попутных улучшений», но если видишь места, где можно что-либо улучшить - скажи.
- По умолчанию: **не менять** архитектуру, публичные типы, сигнатуры, формат/колонки/структуру вывода, поведение — если я явно не попросил.
- Минимальный дифф: сохраняй стиль/структуру/нейминг/порядок, не трогай соседний код без необходимости.

## Ошибки и fail-fast
- Никаких «тихих» дефолтов/заглушек.
- Невозможные/анормальные состояния — **throw** или явный **fail-fast** (как принято в проекте).
- В exception/ошибке указывай ключевой контекст (entryUtc/day-key/tag/counts и т.п.).

## Файлы / перемещения / рефактор
- Не создавай новые файлы и не перемещай существующие без явного запроса.
- Если без этого нельзя — сначала объясни причину и перечисли, что именно будет создано/перемещено.
- Если пришлось делать переименования/миграцию — дай таблицу:
  - устаревшее → замена → где обновлено (файлы/символы)
  - без «полу-миграций».

## Контекст и навигация
- Сначала ищи по репозиторию (rg / переход к определению), не гадай.
- Вопросы задавай только если без них нельзя корректно продолжать.

## Инварианты домена (критично)
- Строго соблюдай границу: **causal ≠ omniscient**.
- Не допускай утечек будущего через данные/типы/зависимости/время.
- Если правка затрагивает эту границу — явно отметь риск в отчёте.

## Режим «идеальная архитектура»
- Включается только если я явно пишу: **«ид арх»**.
- Тогда разрешены ломающие изменения ради строгой типобезопасности и принципа
  «некорректные состояния непредставимы», миграция НЕ постепенная.
- Если не хватает критичного контекста — стоп и конкретный запрос (без «костылей ради компиляции»).

## Формат ответа (обязательно)
1) **Пути изменённых файлов** (точные).
2) По каждому файлу: 1–3 строки «что и зачем».
3) 2–6 ключевых diff-фрагментов **до/после** только для важного.
4) Полные файлы в ответе **не вставляй**, если я явно не попросил.

## Commands (for Codex and humans)

### Build / test
- `dotnet build`
- `dotnet test`

### Targeted tests
- `dotnet test ./SolSignalModel1D_Backtest.Tests/SolSignalModel1D_Backtest.Tests.csproj --filter FullyQualifiedName~<Pattern>`

### Search (ripgrep)
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "<pattern>" .`

### Typical “fix compile errors” loop
1) `dotnet build` (identify failing symbols)
2) `rg` for symbol usage
3) align call-sites to canonical contract owners (time/windowing/split helpers)
4) `dotnet test`

## Карта репозитория (высокий сигнал, низкий шум)

### Факты (по видимым неймспейсам/классам)
- `SolSignalModel1D_Backtest` — консольная оркестрация (partial `Program`), CLI флаги, отчёты.
- `SolSignalModel1D_Backtest.Core` — доменная логика: время/окна, каузальные данные, ML, backtest/PnL, аналитика.
- `SolSignalModel1D_Backtest.SanityChecks` — self-check/leakage проверки (`SelfCheckRunner`).
- `SolSignalModel1D_Backtest.Tests` — xUnit тесты.

## Каноничные владельцы контрактов (ориентиры)

### Время / NY-окна / baseline-exit
- Канон: `NyWindowing` (namespace `SolSignalModel1D_Backtest.Core.Time`).
- Инварианты:
  - входы времени — UTC instant (не day-key);
  - weekend по NY локальному времени для causal запрещён (Try*→false, OrThrow→throw);
  - baseline-exit = следующее NY-утро минус 2 минуты;
  - Friday entry → baseline-exit переносится на понедельник (NY-логика).
- Быстрый поиск:
  - `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "NyWindowing|BaselineExit|NyTradingEntryUtc|EntryDayKeyUtc|ExitDayKeyUtc" .`

### Train/OOS split (baseline-exit day-key)
- Канон: `NyTrainSplit` / `SplitByBaselineExitStrict(...)` (файл встречался как `NyTrainSplit.Strict.cs`).
- Вызовы в оркестрации:
  - `SplitByTrainUntilUtc(...)` (в `Program`).
- Быстрый поиск:
  - `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "NyTrainSplit|SplitByBaselineExitStrict|SplitByTrainUntilUtc" .`

### Daily dataset (для сравнения с split’ом)
- Канон: `DailyDatasetBuilder.Build(...)` (namespace `SolSignalModel1D_Backtest.Core.Causal.ML.Daily`).
- В оркестрации встречается:
  - `DumpDailyAccuracyWithDatasetSplit(...)`.
- Быстрый поиск:
  - `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "DailyDatasetBuilder\\.Build|DumpDailyAccuracyWithDatasetSplit" .`

### Свечи / загрузка / окна backfill
- Оркестрация загрузки:
  - `LoadAllCandlesAndWindow(...)` (partial `Program`): 6h/1h/1m (weekdays + weekends), вычисление `fromUtc/toUtc`.
  - `BootstrapRowsAndCandlesAsync(...)` (partial `Program`): связывает rows + candles и проверяет строгую монотонность.
- Инварианты данных свечей:
  - после init: серии строго возрастающие и строго уникальные по `OpenTimeUtc`;
  - 1m: merge weekdays/weekends без пересечений (`MergeSortedStrictUnique1m`, `EnsureSortedAndStrictUnique1m`);
  - 1m дальше **не** пере-сортировать “на всякий случай” (masking data corruption).
- Быстрый поиск:
  - `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "LoadAllCandlesAndWindow|BootstrapRowsAndCandlesAsync|EnsureSortedAndStrictUnique1m|MergeSortedStrictUnique1m|SeriesGuards" .`

### Gap scan (Binance klines)
- CLI флаги:
  - `--scan-gaps-1m`, `--scan-gaps-1h`, `--scan-gaps-6h` (partial `Program`).
- Канон сканера:
  - `BinanceKlinesGapScanner.ScanGapsAsync(...)` (namespace `SolSignalModel1D_Backtest.Core.Data.Candles.Diagnostics`).
- Быстрый поиск:
  - `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "BinanceKlinesGapScanner|ScanGapsAsync|scan-gaps-" .`

### Индикаторы (FNG/DXY) + coverage fail-fast
- Канон:
  - `IndicatorsDailyUpdater.UpdateAllAsync(...)`
  - `EnsureCoverageOrFail(fromUtc, toUtc)`
- FillMode (важно не менять “тихо”):
  - FNG: `Strict`
  - DXY: `NeutralFill`
- Быстрый поиск:
  - `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "IndicatorsDailyUpdater|EnsureCoverageOrFail|UpdateAllAsync|FillMode|fngFillMode|dxyFillMode" .`

## Доп. контекст проекта (прочитай при старте задачи)
- `.codex/COMMON_COMMANDS.md` — команды build/test/rg и типовой цикл фикса компиляции.
- `.codex/ARCH_GUARDS.md` — инварианты (время, сплиты, каузальность, логирование).
- `.codex/PROJECT_OVERVIEW.md` — назначение репо и смысловые границы.
- `.codex/PROJECT_MAP.md` — карта проектов/папок и стартовые точки поиска.

## Опциональные подсказки (открывать только если задача про соответствующую подсистему)
- `.codex/PLAYBOOKS/Time.NyWindowing.md`
- `.codex/PLAYBOOKS/Data.Candles.Gaps.md`
- `.codex/PLAYBOOKS/Data.Indicators.Coverage.md`
- `.codex/PLAYBOOKS/Debug.TrainSplitMismatch.md`
