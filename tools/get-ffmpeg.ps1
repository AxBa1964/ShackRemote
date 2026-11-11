<#
  Holt die FFmpeg-Bibliotheken (FFmpeg 9.0.1, "full_build-shared" von gyan.dev / GyanD/codexffmpeg)
  und legt die DLLs nach <Repo>\ffmpeg\bin. Der Build kopiert sie dann neben ShackRemote.exe.
  Gleiche Quelle und Version wie das Download-Skript von FFmpeg.AutoGen 9.0.1.1.
#>
param([string]$Version = "9.0.1", [switch]$Force)
$ErrorActionPreference = "Stop"

$Root   = Split-Path -Parent $PSScriptRoot
$BinDir = Join-Path $Root "ffmpeg\bin"
if ((Test-Path (Join-Path $BinDir "avcodec-63.dll")) -and -not $Force) {
    Write-Host "FFmpeg-DLLs sind schon da: $BinDir  (neu laden mit -Force)"
    exit 0
}

$Name = "ffmpeg-$Version-full_build-shared"
$Url  = "https://github.com/GyanD/codexffmpeg/releases/download/$Version/$Name.zip"
$Tmp  = Join-Path $env:TEMP "shackremote-ffmpeg"
New-Item -ItemType Directory -Force -Path $Tmp, $BinDir | Out-Null

Write-Host "Lade $Url (ca. 95 MB) ..."
$ProgressPreference = "SilentlyContinue"   # sonst ist Invoke-WebRequest sehr langsam
Invoke-WebRequest -Uri $Url -OutFile (Join-Path $Tmp "$Name.zip") -UseBasicParsing

Write-Host "Entpacke ..."
Expand-Archive -Path (Join-Path $Tmp "$Name.zip") -DestinationPath $Tmp -Force
Copy-Item (Join-Path $Tmp "$Name\bin\*.dll") $BinDir -Force
Remove-Item -Recurse -Force $Tmp

Get-ChildItem $BinDir -Filter *.dll | ForEach-Object { Write-Host ("  {0,-22} {1,8:N0} KB" -f $_.Name, ($_.Length / 1KB)) }
Write-Host "Fertig."
