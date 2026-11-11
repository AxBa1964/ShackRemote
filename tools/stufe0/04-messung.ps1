# ------------------------------------------------------------------
# ShackRemote Stufe 0 - Last automatisch messen (CPU und RAM)
# Parallel zu 01-aufnahme-lokal.cmd oder 02-stream-server.cmd starten.
# Aufruf:  .\04-messung.cmd [Sekunden] [Bezeichnung]
# Beispiel: .\04-messung.cmd 30 "Stream Monitor 1 mit WSJT-X"
# Ergebnis wird angezeigt und an messung.csv angehaengt.
# ------------------------------------------------------------------
param(
    [int]$Sekunden = 30,
    [string]$Bezeichnung = "ohne Bezeichnung"
)

$cpuWerte = New-Object System.Collections.Generic.List[double]
$ramWerte = New-Object System.Collections.Generic.List[double]

Write-Host "Messe $Sekunden Sekunden: $Bezeichnung"
for ($i = 1; $i -le $Sekunden; $i++) {
    $cpu = (Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'").PercentProcessorTime
    $os  = Get-CimInstance Win32_OperatingSystem
    $ram = [math]::Round((1 - $os.FreePhysicalMemory / $os.TotalVisibleMemorySize) * 100, 1)
    $cpuWerte.Add([double]$cpu)
    $ramWerte.Add($ram)
    Write-Host ("{0,3} s   CPU {1,3} %   RAM {2,5} %" -f $i, $cpu, $ram)
    Start-Sleep -Seconds 1
}

$cpuMittel = [math]::Round(($cpuWerte | Measure-Object -Average).Average, 1)
$cpuMax    = ($cpuWerte | Measure-Object -Maximum).Maximum
$ramMittel = [math]::Round(($ramWerte | Measure-Object -Average).Average, 1)
$ramMax    = ($ramWerte | Measure-Object -Maximum).Maximum

Write-Host ""
Write-Host "Ergebnis: CPU Mittel $cpuMittel %, Max $cpuMax %   |   RAM Mittel $ramMittel %, Max $ramMax %"

$csv = Join-Path $PSScriptRoot "messung.csv"
if (-not (Test-Path $csv)) {
    "Zeit;Bezeichnung;Sekunden;CPU_Mittel;CPU_Max;RAM_Mittel;RAM_Max" | Out-File $csv -Encoding utf8
}
("{0};{1};{2};{3};{4};{5};{6}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Bezeichnung, $Sekunden, $cpuMittel, $cpuMax, $ramMittel, $ramMax) |
    Out-File $csv -Append -Encoding utf8
Write-Host "Angehaengt an $csv"
