# Controleert de taalbestanden (lang\*.json) tegen de teksten in de code.
#   powershell -File tools\check-lang.ps1            (alle talen)
#   powershell -File tools\check-lang.ps1 -Lang nl   (één taal)
# Toont per taal: teksten in de code zonder vertaling, vertalingen die niet meer in de code voorkomen en
# vertalingen waarvan de {0}, {1}… niet kloppen. Exitcode 1 bij fouten (ontbrekend/ongebruikt telt als waarschuwing).
param([string]$Lang = "")

$root = Split-Path -Parent $PSScriptRoot
$src = Get-ChildItem (Join-Path $root "src") -Filter *.cs
$code = ($src | ForEach-Object { Get-Content $_.FullName -Raw -Encoding UTF8 }) -join "`n"

# Sleutels: Loc.T("...") en Loc.N("...") (de Engelse tekst); C#-escapes worden via de JSON-parser omgezet.
$keys = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::Ordinal)
foreach ($m in [regex]::Matches($code, 'Loc\.[TN]\("((?:[^"\\]|\\.)*)"')) {
    try { $k = ConvertFrom-Json ('"' + $m.Groups[1].Value + '"') } catch { continue }
    [void]$keys.Add($k)
}
# Regels in de lange Engelse tekst van het Over-scherm (raw string) worden per regel vertaald.
$about = Get-Content (Join-Path $root "src\AboutForm.cs") -Raw -Encoding UTF8
if ($about -match '(?s)BodyEn = """\r?\n(.*?)"""') {
    foreach ($line in ($Matches[1] -split "\r?\n")) { if ($line.Trim().Length -gt 0) { [void]$keys.Add($line.Substring([Math]::Min(8, $line.Length - $line.TrimStart().Length))) } }
}

function Placeholders($s) { ([regex]::Matches($s, '\{(\d+)')) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique }

$files = Get-ChildItem (Join-Path $root "lang") -Filter *.json
if ($Lang) { $files = $files | Where-Object { $_.BaseName -eq $Lang } }
$fail = 0
foreach ($f in $files) {
    Add-Type -AssemblyName System.Web.Extensions
    $ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer; $ser.MaxJsonLength = 100MB
    $t = $ser.DeserializeObject((Get-Content $f.FullName -Raw -Encoding UTF8))
    $names = [string[]]$t.Keys
    $missing = @($keys | Where-Object { -not $t.ContainsKey([string]$_) })
    $unused = @($names | Where-Object { $_ -ne "_name" -and -not $keys.Contains([string]$_) -and -not $code.Contains($_) -and -not $code.Contains($_.Replace('"', '\"')) })
    $badPh = @($names | Where-Object { $_ -ne "_name" -and ((Placeholders $_) -join ",") -ne ((Placeholders $t[$_]) -join ",") })
    "{0}: {1} vertalingen, {2} ontbreken, {3} ongebruikt, {4} met foute plaatsaanduidingen" -f $f.BaseName, ($names.Count - 1), $missing.Count, $unused.Count, $badPh.Count
    $missing | Select-Object -First 15 | ForEach-Object { "  ONTBREEKT: $_" }
    $unused | Select-Object -First 15 | ForEach-Object { "  ONGEBRUIKT: $_" }
    $badPh | ForEach-Object { "  FOUT: $_" }
    if ($badPh.Count -gt 0) { $fail = 1 }
}
exit $fail
