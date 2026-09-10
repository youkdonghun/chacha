param([switch]$Launch, [switch]$CtrlShift)
$ErrorActionPreference = 'Stop'
$fixtureRoot = Split-Path -Parent $PSScriptRoot
$fixtureOutput = Join-Path $fixtureRoot 'obj\input-fixture'
New-Item -ItemType Directory -Force -Path $fixtureOutput | Out-Null
$fixtureCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fixtureExe = Join-Path $fixtureOutput 'InputFixture.exe'
& $fixtureCompiler /nologo /target:winexe /platform:x64 /langversion:5 /codepage:65001 /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ('/out:' + $fixtureExe) (Join-Path $PSScriptRoot 'InputFixture.cs')
if ($LASTEXITCODE -ne 0) { throw 'Input fixture compilation failed.' }
Write-Output $fixtureExe
if ($Launch) {
    if ($CtrlShift) { Start-Process -FilePath $fixtureExe -ArgumentList '--ctrl-shift' -WindowStyle Hidden }
    else { Start-Process -FilePath $fixtureExe -WindowStyle Hidden }
}
