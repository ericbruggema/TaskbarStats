# Tests

xUnit-project `TaskbarStats.Tests` (net8.0-windows) dat `TaskbarStats.csproj` refereert; de app geeft zijn `internal`-leden
door met `InternalsVisibleTo`.

```
dotnet test tests\TaskbarStats.Tests -c Release
dotnet test tests\TaskbarStats.Tests -c Release -p:SkipLangCheck=true   # sneller: zonder de LangTool-controle bij de build
```

Wat er getest wordt: `Loc` (vertalen, context, meervoud, pseudotaal, regiotalen, terugval), `AppSettings` (opslaan/laden,
oude/kapotte json, atomair opslaan), thema's en `Tiles`, `UpdateChecker` (versievergelijking, JSON, `TASKBARSTATS_UPDATE_URL`
met een bestand), `UsageTracker`, `Diag` en de `LangTool` zelf (repo slaagt met `--strict`; kapotte kopie geeft exitcode 1).

Afspraken: geen vensters, geen netwerk, geen UAC en nooit de echte `%AppData%\TaskbarStats`; elke test zet
`TASKBARSTATS_DATA` op een tijdelijke map (`TempData`). Loc en Diag hebben processbrede toestand, daarom draaien de tests
niet parallel. De LangTool-tests starten `dotnet run` (enkele tot tientallen seconden).

CI: `.github\workflows\ci.yml` bouwt, test en draait `LangTool check --strict` op Windows.
