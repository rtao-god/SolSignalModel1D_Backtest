# Commands (for Codex and humans)

## Build / test
- dotnet build SolSignalModel1D_Backtest
- dotnet test

## Targeted tests (example patterns)
- dotnet test ./SolSignalModel1D_Backtest.Tests/SolSignalModel1D_Backtest.Tests.csproj --filter FullyQualifiedName~<Pattern>

## Search
- rg -n --glob "!**/bin/**" --glob "!**/obj/**" "<pattern>" .

# Поиск всех вызовов baseline-exit
rg -n 'BaselineExitOrThrow' --glob "!**/bin/**" --glob "!**/obj/**"

# Поиск всех вызовов SplitByBaselineExitStrict
rg -n 'SplitByBaselineExitStrict\s*\<' --glob "!**/bin/**" --glob "!**/obj/**"

# Быстрый log вызова по context tag
rg -i --context 2 'tag\s*=' --glob "!**/bin/**" --glob "!**/obj/**"

## Typical “fix compile errors” loop
1) dotnet build (identify failing symbols)
2) rg for the symbol usage
3) align call-sites to canonical contract owners (NyWindowing / split helpers / time types)
4) dotnet test (ensure no invariants broken)
