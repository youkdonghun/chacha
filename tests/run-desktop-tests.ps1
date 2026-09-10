param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $taskRoot 'build.ps1') -Test }
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskExe = Join-Path $taskRoot 'dist\DesktopIntegration.exe'
$taskReport = Join-Path $taskRoot 'artifacts\desktop-integration-v1.3.txt'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $taskReport) | Out-Null
& $taskCompiler /nologo /target:exe /platform:x64 /langversion:5 /codepage:65001 /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ('/reference:' + (Join-Path $taskRoot 'dist\ChachaCapture.exe')) ("/out:" + $taskExe) (Join-Path $PSScriptRoot 'DesktopIntegration.cs')
if ($LASTEXITCODE -ne 0) { throw 'Desktop QA compilation failed.' }
$taskProcess = Start-Process -FilePath $taskExe -ArgumentList ('"' + $taskReport + '"') -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath $taskReport
if ($taskProcess.ExitCode -ne 0) { throw 'Desktop QA failed.' }
