# Verouderd: de controle zit nu in tools\LangTool (leest de C#-code echt met Roslyn) en draait ook bij elke build.
#   powershell -File tools\check-lang.ps1 [-Strict]      = dotnet run --project tools\LangTool -- check [--strict]
# Meer opdrachten: dotnet run --project tools\LangTool -- sync | rename | new | status
param([switch]$Strict)
$root = Split-Path -Parent $PSScriptRoot
$a = @("run", "--project", (Join-Path $PSScriptRoot "LangTool\LangTool.csproj"), "-c", "Release", "--verbosity", "quiet", "--", "check", "--root", $root)
if ($Strict) { $a += "--strict" }
& dotnet @a
exit $LASTEXITCODE
