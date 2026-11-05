param([switch]$Best,[switch]$Refined,[switch]$HeldOut)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $taskRoot 'obj\ocr-payload\Tesseract.dll'))) { throw 'Run .\build.ps1 before the OCR comparison.' }
if ($Best -and -not (Test-Path -LiteralPath (Join-Path $taskRoot 'obj\ocr-dependencies\kor.traineddata'))) { throw '-Best is an optional research comparison and needs the separately downloaded tessdata_best Korean model in obj\ocr-dependencies\kor.traineddata. The app does not use this model.' }
New-Item -ItemType Directory -Force -Path (Join-Path $taskRoot 'artifacts') | Out-Null
$taskFramework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$taskMetadata=Join-Path $env:WINDIR 'System32\WinMetadata'
$taskReferences=@('System.dll','System.Core.dll','System.Drawing.dll','System.Web.Extensions.dll',"$taskFramework\System.Runtime.WindowsRuntime.dll",(Join-Path $taskRoot 'obj\ocr-payload\Tesseract.dll'))
foreach($taskName in @('Windows.Foundation','Windows.Media','Windows.Globalization','Windows.Storage','Windows.Graphics')) { $taskReferences+=Join-Path $taskMetadata ($taskName+'.winmd') }
foreach($taskName in @('System.Runtime','System.Runtime.InteropServices.WindowsRuntime','System.Threading.Tasks')) { $taskReferences+=(Get-ChildItem -LiteralPath (Join-Path "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL" $taskName) -Recurse -Filter ($taskName+'.dll') | Select-Object -First 1).FullName }
$taskArgs=@('/nologo','/target:exe','/platform:x64','/langversion:5','/codepage:65001',('/out:'+(Join-Path $taskRoot 'obj\ocr-payload\OcrAccuracyBench.exe')))
$taskArgs+=@($taskReferences | ForEach-Object { '/reference:'+$_ })
& "$taskFramework\csc.exe" @taskArgs (Join-Path $taskRoot 'tests\OcrAccuracyBenchmark.cs') (Join-Path $taskRoot 'src\OcrPreprocessing.cs') (Join-Path $taskRoot 'ocr\OcrRecognition.cs')
if($LASTEXITCODE -ne 0) { throw 'OCR comparison compile failed' }
$env:OMP_THREAD_LIMIT='2'
if($HeldOut) {
    & (Join-Path $taskRoot 'obj\ocr-payload\OcrAccuracyBench.exe') (Join-Path $taskRoot 'obj\ocr-payload\tessdata') (Join-Path $taskRoot 'artifacts\ocr-accuracy-heldout.json') (Join-Path $taskRoot 'artifacts\ocr-accuracy-heldout.png') refine heldout
} elseif($Refined) {
    & (Join-Path $taskRoot 'obj\ocr-payload\OcrAccuracyBench.exe') (Join-Path $taskRoot 'obj\ocr-payload\tessdata') (Join-Path $taskRoot 'artifacts\ocr-accuracy-refined.json') (Join-Path $taskRoot 'artifacts\ocr-accuracy-sample.png') refine
} elseif($Best) {
    & (Join-Path $taskRoot 'obj\ocr-payload\OcrAccuracyBench.exe') (Join-Path $taskRoot 'obj\ocr-dependencies') (Join-Path $taskRoot 'artifacts\ocr-accuracy-best.json') (Join-Path $taskRoot 'artifacts\ocr-accuracy-sample.png') best
} else {
    & (Join-Path $taskRoot 'obj\ocr-payload\OcrAccuracyBench.exe') (Join-Path $taskRoot 'obj\ocr-payload\tessdata') (Join-Path $taskRoot 'artifacts\ocr-accuracy-comparison.json') (Join-Path $taskRoot 'artifacts\ocr-accuracy-sample.png')
}
if($LASTEXITCODE -ne 0) { throw 'OCR comparison failed' }
