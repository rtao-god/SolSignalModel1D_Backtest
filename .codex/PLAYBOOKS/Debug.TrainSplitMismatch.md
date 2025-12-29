# Playbook: дебаг рассинхрона train/oos split (dataset-based vs record-based)

## Когда открывать
- Есть расхождение между:
  - split, построенным датасет-билдером (`DailyDatasetBuilder.Build(...)`),
  - split, построенным по записям (`BacktestRecord`) в оркестрации.
- Симптомы: метрики train/oos «не бьются», особенно вокруг пятниц/понедельников.

## Наблюдаемая точка в оркестрации
- `DumpDailyAccuracyWithDatasetSplit(allRows, records, trainUntilUtc)`:
  - строит dataset по `trainUntilExitDayKeyUtc`,
  - затем выбирает `trainDates` из `dataset.TrainRows.Select(r => r.EntryDayKeyUtc.Value)`,
  - потом фильтрует `records` по `r.EntryDayKeyUtc.Value`.

Это место подозрительно, если датасет внутри делит по baseline-exit, а наружу сравнивают по entry-day-key.

## Канон split’а
- Канон: split по **baseline-exit day-key**, а не по entry-day.
- Канон владелец: `NyTrainSplit` + `NyWindowing`.

## Как искать корень
1) Открыть реализацию `DailyDatasetBuilder.Build(...)` и подтвердить:
   - по какому ключу она классифицирует train/oos (entry-day-key или baseline-exit-day-key).
2) Открыть `NyTrainSplit.SplitByBaselineExitStrict(...)` и подтвердить:
   - что порог сравнивается с baseline-exit.
3) Сравнить ключи, которыми матчится `dataset.TrainRows` и `records` в `DumpDailyAccuracyWithDatasetSplit`.

Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "DumpDailyAccuracyWithDatasetSplit|DailyDatasetBuilder\\.Build|SplitByBaselineExitStrict" .`

## Типовой root-cause (если подтвердится пункт 1)
- Датасет делит по baseline-exit day-key, но внешняя проверка матчится по entry-day-key:
  - на пятницу baseline-exit уходит на понедельник,
  - entry-day-key и baseline-exit-day-key расходятся,
  - выборка по entry-day-key становится логически неверной.

## Мини-чеклист исправления (после подтверждения реализаций)
- В debug/проверках матчить записи и датасет по **одному и тому же смысловому ключу**:
  - либо по baseline-exit day-key,
  - либо (если датасет реально по entry-day-key) — тогда split по записям должен быть приведён к entry-day-key (что противоречит канону и требует отдельного решения).
- На границах TrainUntil всегда использовать `ExitDayKeyUtc.FromUtcMomentOrThrow(trainUntilUtc)` (или эквивалент владельца).
