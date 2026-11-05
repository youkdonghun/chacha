$ErrorActionPreference = 'Stop'
$ocrRoot = Split-Path -Parent $PSScriptRoot
$ocrDependencies = Join-Path $ocrRoot 'obj\ocr-dependencies'
$ocrStage = Join-Path $ocrRoot 'obj\ocr-payload'
New-Item -ItemType Directory -Force -Path $ocrDependencies, $ocrStage, "$ocrStage\x64", "$ocrStage\tessdata", "$ocrStage\tessdata\accurate" | Out-Null
function Get-OcrDependency($Name, $Uri, $Hash) {
    $target = Join-Path $ocrDependencies $Name
    if (-not (Test-Path -LiteralPath $target)) {
        Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile ($target + '.download')
        if ((Get-FileHash -LiteralPath ($target + '.download') -Algorithm SHA256).Hash -ne $Hash) { throw "OCR dependency hash mismatch: $Name" }
        Move-Item -LiteralPath ($target + '.download') -Destination $target
    }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $Hash) { throw "OCR dependency hash mismatch: $Name" }
    return $target
}
$ocrPackage = Get-OcrDependency 'tesseract.5.2.0.nupkg' 'https://api.nuget.org/v3-flatcontainer/tesseract/5.2.0/tesseract.5.2.0.nupkg' '202D82FC7C7D8384DF7DA57206D5E1F456CCDABD648C46E67CDFAA3A911D4795'
$ocrCommit = '87416418657359cb625c412a48b6e1d6d41c29bd'
$ocrKor = Get-OcrDependency 'kor-fast.traineddata' "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/$ocrCommit/kor.traineddata" '6B85E11D9BBF07863B97B3523B1B112844C43E713DF8B66418A081FD1060B3B2'
$ocrEng = Get-OcrDependency 'eng-fast.traineddata' "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/$ocrCommit/eng.traineddata" '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2'
$ocrBestCommit = 'e12c65a915945e4c28e237a9b52bc4a8f39a0cec'
$ocrEngAccurate = Get-OcrDependency 'eng.traineddata' "https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/$ocrBestCommit/eng.traineddata" '8280AED0782FE27257A68EA10FE7EF324CA0F8D85BD2FD145D1C2B560BCB66BA'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$ocrZip = [IO.Compression.ZipFile]::OpenRead($ocrPackage)
try {
    foreach ($pair in @(@('lib/net48/Tesseract.dll','Tesseract.dll'), @('x64/leptonica-1.82.0.dll','x64\leptonica-1.82.0.dll'), @('x64/tesseract50.dll','x64\tesseract50.dll'))) {
        $entry = $ocrZip.GetEntry($pair[0]); if ($null -eq $entry) { throw "Missing OCR package entry: $($pair[0])" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $ocrStage $pair[1]), $true)
    }
} finally { $ocrZip.Dispose() }
Copy-Item -LiteralPath $ocrKor -Destination "$ocrStage\tessdata\kor.traineddata" -Force
Copy-Item -LiteralPath $ocrEng -Destination "$ocrStage\tessdata\eng.traineddata" -Force
Copy-Item -LiteralPath $ocrEngAccurate -Destination "$ocrStage\tessdata\accurate\eng.traineddata" -Force
Copy-Item -LiteralPath (Join-Path $ocrRoot 'THIRD-PARTY-NOTICES.txt') -Destination $ocrStage -Force
$ocrCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $ocrCompiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /codepage:65001 /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/reference:$ocrStage\Tesseract.dll" "/out:$ocrStage\ChachaOcr.exe" (Join-Path $ocrRoot 'ocr\Worker.cs') (Join-Path $ocrRoot 'ocr\OcrRecognition.cs') (Join-Path $ocrRoot 'src\OcrPreprocessing.cs')
if ($LASTEXITCODE -ne 0) { throw 'OCR worker compilation failed.' }
$ocrPayload = Join-Path $ocrRoot 'obj\ocr-payload.zip'
Compress-Archive -LiteralPath @("$ocrStage\ChachaOcr.exe", "$ocrStage\Tesseract.dll", "$ocrStage\x64", "$ocrStage\tessdata", "$ocrStage\THIRD-PARTY-NOTICES.txt") -DestinationPath $ocrPayload -Force
