# Windows 텍스트 복사와 Chacha OCR 비교

2026-11-05에 확인했습니다. 사용자가 v1.5.2에서 캡처 자체는 정상 동작한다고 확인한 뒤, 숫자·영문 혼동을 별도로 조사했습니다.

## Windows 캡처도구

Microsoft의 [지원 문서](https://support.microsoft.com/en-gb/windows/apps/use-snipping-tool-to-capture-screenshots?nochrome=true)는 이미지에서 텍스트 선택·전체 복사·이메일/전화번호 가리기를 설명하며, 인식은 기기 안에서 처리한다고 안내합니다. [2025-04-15 기능 발표](https://blogs.windows.com/windows-insider/2025/04/15/text-extractor-in-snipping-tool-begins-rolling-out-to-windows-insiders/)에는 캡처 막대에서 영역 지정 후 텍스트를 복사하고, 줄바꿈 제거·자동 복사 옵션을 사용하는 흐름이 있습니다.

이 PC의 캡처도구 11.2608.41.0에서 합성 이미지를 열고 ‘텍스트 스캔’, 이미지 위의 텍스트 선택 영역, ‘모든 텍스트 복사’, 표 복사(이 표본에서는 비활성)를 직접 확인했습니다. 복사된 전체 문자열의 자동 비교는 완료하지 못했으므로 아래 수치는 캡처도구 앱 자체의 정확도 비교가 아닙니다.

[Windows.Media.Ocr.OcrEngine](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine?view=winrt-26100)은 공개 API지만, 캡처도구의 내부 엔진과 같다고 확인된 것은 아닙니다. 이 환경의 설치 언어는 `ko`였습니다. [Windows AI TextRecognizer](https://learn.microsoft.com/en-us/windows/ai/apis/text-recognition)는 NPU 요구 사항이 있어 일반 x64 EXE의 필수 경로로 선택하지 않았습니다. 캡처도구의 비공개 바이너리를 추출하거나 재배포하지 않았습니다.

## 같은 이미지로 측정

맑은 고딕·Segoe UI·Consolas, 14/18/24px, 밝은/어두운 배경의 18개 조건을 사용했습니다. 수량·금액·시간·이메일과 `S8S5`, `0O01`, `O0O0`, `B8B8`, `I1l1`을 포함합니다. 값은 모든 표본의 **공백을 제외한 문자 편집 거리 합계**이며 낮을수록 좋습니다. 코드·기호가 일부러 어려운 표본이므로 일반 정확도로 해석하지 않습니다.

| 방식 | 문자 편집 거리 합계 |
|---|---:|
| v1.5.2 혼합 인식, 2배 확대 | 244 |
| Windows 공개 OCR API, 동일 전처리 | 260 |
| 고정밀 한글+영문 모델 전체 교체 | 260 |
| v1.5.3 혼합 인식 후 영문·숫자 재인식 | 169 |

Windows 공개 API는 일부 숫자를 잘 읽었지만 `%`가 `96`으로 바뀌는 등 다른 오류가 있었습니다. 큰 모델로 전부 바꾸는 방식도 이 표본에서는 개선되지 않았습니다.

재인식 알고리즘은 동일 위치의 단어를 겹침 비율로 대응시키며 한글을 포함한 단어를 바꾸지 않습니다. 순수 숫자에서 소문자 영단어로 바꾸거나 구두점 구성을 바꾸는 후보는 자동 적용하지 않습니다. 결과가 달랐던 후보는 사용자에게 표시합니다. 숫자/문자 일괄 치환은 없습니다.

위 18개 중 문자 오류가 늘어난 조건은 0개였고, 숫자 행은 전후 모두 18/18 일치했습니다. 이후 **다른 문장·Arial/Tahoma·16/22px·밝은/어두운 배경** 8개 조건에서도 67→56으로 줄었고, 오류가 늘어난 조건은 0개였습니다. 이는 사용자가 실제로 읽었던 원본 화면을 검증한 결과는 아닙니다.

## 전용 읽기 방식과 한계

24px 세 글꼴·두 배경의 별도 표본에서 숫자 모드의 전체 행은 고정밀 영문 모델 적용 후 6/6 일치했습니다(기존 fast 모델 5/6). 영문 모드의 코드·숫자 접두부는 6/6이지만 이메일까지 포함한 전체 행은 4/6이었습니다. Consolas의 `test08`이 `testo8`로 읽히는 문제가 남습니다. 한글 문장 일부와 고립된 기호, 0/O/o·1/I/l처럼 모양이 비슷한 문자도 항상 맞는다고 보장할 수 없습니다.

번호·금액에는 ‘숫자·기호만’을 선택할 수 있습니다. 이 선택은 원본에 문자가 없다는 사용자의 의도를 인식기에 전달하며, 혼합 코드에 적용하면 문자가 제외되므로 UI에서 명시합니다.

재인식은 기존 fast 모델을 사용하고, 전용 영문·숫자 모드만 `tessdata_best` 영문 모델(커밋 `e12c65a915945e4c28e237a9b52bc4a8f39a0cec`, SHA-256 `8280aed0782fe27257a68ea10fe7ef324ca0f8d85bd2fd145d1c2b560bcb66ba`)을 사용합니다. 모델은 실행파일에 포함되며 화면과 결과를 서버로 보내지 않습니다.

원시 측정 결과는 v1.5.3 릴리스의 `ocr-accuracy-refined.json`, `ocr-accuracy-heldout.json`, `ocr-profile-comparison.json`, `ocr-accuracy-results.json`에 제공합니다.

Windows 공개 API 비교는 빌드 후 `tests/run-ocr-accuracy-benchmark.ps1 -Refined`와 `-HeldOut`으로 재현할 수 있습니다. Windows OCR 한국어 언어 팩과 Windows SDK 메타데이터가 있는 환경을 사용합니다. `-Best`는 별도로 준비한 고정밀 한국어 모델이 필요한 연구용 비교이며 일반 빌드에는 필요하지 않습니다.
