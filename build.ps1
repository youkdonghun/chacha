param([switch]$Test)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'dist'
$taskObj = Join-Path $taskRoot 'obj'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Windows x64 with .NET Framework 4.8 is required.' }
New-Item -ItemType Directory -Force -Path $taskOutput, $taskObj | Out-Null
$taskIconBuilder = Join-Path $taskObj 'GenerateIcon.exe'
$taskIcon = Join-Path $taskObj 'ChachaCapture.ico'
& $taskCompiler /nologo /target:exe /platform:x64 /reference:System.Drawing.dll "/out:$taskIconBuilder" (Join-Path $taskRoot 'build\GenerateIcon.cs')
if ($LASTEXITCODE -ne 0) { throw 'Icon tool compilation failed.' }
& $taskIconBuilder $taskIcon
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$taskExe = Join-Path $taskOutput 'ChachaCapture.exe'
$taskFramework = Split-Path -Parent $taskCompiler
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ /langversion:5 /codepage:65001 /warn:4 "/out:$taskExe" "/win32manifest:$(Join-Path $taskRoot 'src\app.manifest')" "/win32icon:$taskIcon" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/reference:$taskFramework\WPF\UIAutomationClient.dll" "/reference:$taskFramework\WPF\UIAutomationTypes.dll" "/reference:$taskFramework\WPF\WindowsBase.dll" $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
if ($Test) {
    $taskReport = Join-Path $taskOutput 'self-test-results.json'
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList @('--self-test', '--test-output', ('"' + $taskReport + '"')) -WindowStyle Hidden -Wait -PassThru
    if (Test-Path -LiteralPath $taskReport) { Get-Content -LiteralPath $taskReport }
    if ($taskProcess.ExitCode -ne 0) { throw "Self tests failed: $($taskProcess.ExitCode)" }
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $taskOutput 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination (Join-Path $taskOutput 'LICENSE') -Force
$taskDocs = Join-Path $taskOutput 'docs'
New-Item -ItemType Directory -Force -Path $taskDocs | Out-Null
Copy-Item -LiteralPath @((Join-Path $taskRoot 'docs\PARITY.md'), (Join-Path $taskRoot 'docs\VALIDATION.md'), (Join-Path $taskRoot 'docs\RELEASE-v1.4.0.md')) -Destination $taskDocs -Force
$taskHash = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $taskOutput 'SHA256SUMS.txt') -Encoding Ascii -Value ($taskHash + '  ChachaCapture.exe')
$taskArchive = Join-Path $taskOutput 'ChachaCapture-1.4.0-win-x64.zip'
Compress-Archive -LiteralPath @($taskExe, (Join-Path $taskOutput 'README.md'), (Join-Path $taskOutput 'LICENSE'), (Join-Path $taskOutput 'SHA256SUMS.txt'), $taskDocs) -DestinationPath $taskArchive -Force
Write-Output "Built Windows x64: $taskExe"
Write-Output "Portable archive: $taskArchive"
