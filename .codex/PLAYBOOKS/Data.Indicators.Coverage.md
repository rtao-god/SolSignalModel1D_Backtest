# Playbook: индикаторы (FNG/DXY), UpdateAllAsync и coverage

## Когда открывать
- Любые задачи про обновление дневных индикаторов, пропуски, fill modes.
- Любые задачи, где пайплайн должен падать на «дырявых» рядах до построения датасетов/моделей.

## Наблюдаемые элементы в оркестрации
- `IndicatorsDailyUpdater.UpdateAllAsync(...)`
  - `fngFillMode: Strict`
  - `dxyFillMode: NeutralFill`
- `EnsureCoverageOrFail(fromUtc, toUtc)` как gate (fail-fast).

Поиск:
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "IndicatorsDailyUpdater|UpdateAllAsync|EnsureCoverageOrFail|fngFillMode|dxyFillMode|FillMode" .`

## Инварианты
- Coverage проверяется до продолжения пайплайна (иначе downstream-ошибки будут симптомами).
- FillMode не должен «тихо» меняться ради прохождения пайплайна:
  это меняет семантику и может скрыть проблемы данных.

## Root-cause типовых проблем coverage
- Провалы внешних источников/HTTP, частичная загрузка, неправильный диапазон `fromUtc/toUtc`.
- Заполнение neutral-fill применяется не там/не тем индикатором.
- Несогласованность day-key нормализации (`ToCausalDateUtc`/day-key derivation) между индикаторами и свечами.

## Мини-чеклист при отладке
- Подтвердить, что `fromUtc/toUtc` вычислены ожидаемо и нормализованы одинаково во всех источниках.
- Убедиться, что `EnsureCoverageOrFail` действительно вызывает fail-fast на пропусках.
- Проверить, что FNG остаётся `Strict` (не подменён «ради прохода»).
