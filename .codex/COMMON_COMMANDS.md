# Commands (for Codex and humans)

## Build / test
- `dotnet build`
- `dotnet test`

## Targeted tests (example patterns)
- `dotnet test ./SolSignalModel1D_Backtest.Tests/SolSignalModel1D_Backtest.Tests.csproj --filter FullyQualifiedName~<Pattern>`

## Search (ripgrep)
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "<pattern>" .`

## Частые поиски по подсистемам

### NY окна / baseline-exit / DST
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "NyWindowing|BaselineExit|NyTradingEntryUtc" .`

### Split по baseline-exit
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "NyTrainSplit|SplitByBaselineExitStrict|ClassifyByBaselineExit" .`

### Daily dataset builder
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "DailyDatasetBuilder\\.Build" .`

### Gap scan (CLI и сканер)
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "scan-gaps-|BinanceKlinesGapScanner|ScanGapsAsync" .`

### Индикаторы (FNG/DXY) и coverage
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "IndicatorsDailyUpdater|EnsureCoverageOrFail|UpdateAllAsync|FillMode" .`

### Свечи: источники/merge/инварианты
- `rg -n --glob "!**/bin/**" --glob "!**/obj/**" "LoadAllCandlesAndWindow|CandleResampler|CandleNdjsonStore|CandlePaths\\.WeekendFile|EnsureSortedAndStrictUnique1m|MergeSortedStrictUnique1m|SeriesGuards" .`

## Typical “fix compile errors” loop
1) `dotnet build SolSignalModel1D_Backtest` (взять failing symbol / CSxxxx)
2) `rg` по символу / методу
3) найти владельца контракта (time/windowing/split) и привести call-sites к нему
4) `dotnet test` (проверить инварианты/регрессии)
