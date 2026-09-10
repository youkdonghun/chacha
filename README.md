# Chacha Capture

Windows x64용 캡처·주석·화면 고정 프로그램입니다. Snipaste Windows 무료판의 조작 흐름을 기준으로 직접 구현했습니다. 원본 소프트웨어의 코드·로고·리소스는 사용하지 않습니다.

## 다운로드 및 실행

[최신 릴리스](https://github.com/youkdonghun/chacha/releases/latest)에서 **ChachaCapture.exe**를 받아 실행하세요.
Windows 10/11 x64와 .NET Framework 4.8을 사용합니다. 별도 설치나 관리자 권한 없이 EXE 하나로 실행됩니다.
창을 닫으면 트레이에서 계속 실행합니다. 완전히 종료하려면 트레이 우클릭 → 종료를 사용하세요.

## 앱 안에서 업데이트

대시보드의 **업데이트** 또는 트레이 메뉴의 **업데이트 확인**을 누릅니다.
GitHub 최신 정식 버전이 있으면 **다운로드 → 설치하고 다시 시작**으로 현재 EXE를 교체합니다.
다운로드한 파일의 SHA-256과 64비트 실행 파일 형식을 확인하며, 교체에 실패하면 기존 파일을 복구합니다.
설정과 캡처·플로팅 이미지는 유지합니다. 확인·다운로드는 취소할 수 있으며 새 버전을 자동으로 설치하지 않습니다.
v1.1 이하에는 이 메뉴가 없으므로 v1.2 EXE를 한 번 직접 다운로드한 뒤 사용할 수 있습니다.
현재 저장소는 비공개입니다. 업데이트에는 해당 저장소를 읽을 수 있는 GitHub 인증이 필요하며,
이 PC의 GitHub CLI 로그인 또는 `GH_TOKEN` / `GITHUB_TOKEN` 환경 변수를 사용합니다.
다른 PC에서는 GitHub CLI에서 `gh auth login`으로 로그인하면 됩니다. 인증값은 실행 파일·설정·로그에 저장하지 않습니다.

## 캡처

| 동작 | 조작 |
|---|---|
| 캡처 시작 | **F1**, 트레이 왼쪽 클릭 |
| 영역 선택 | 드래그 또는 자동 감지된 창·요소 클릭 |
| 복사하고 플로팅 창으로 띄우기 | **Enter**, **Ctrl+C**, 영역 더블클릭 |
| 바로 플로팅 | **Ctrl+T**, 영역 가운데 클릭, **화면에 띄우기** 버튼 |
| 저장 / 빠른 저장 / 인쇄 | **Ctrl+S / Ctrl+Shift+S / Ctrl+P** |
| 전체 화면 / 마지막 성공 영역 | **Ctrl+A / R** |
| 캡처 기록 이전 / 다음 | **, / .** |
| 창 / UI 요소 감지 전환 | **Tab**, 선택 전 휠로 겹친 영역 탐색 |
| 영역 이동 / 확대 / 축소 | **방향키 / Ctrl+방향키 / Shift+방향키** |
| 커서 1픽셀 이동 | **W A S D** |
| 캡처한 커서 표시 전환 | **`** |
| 확대경 / 색상 형식 / 색상 복사 | **Alt / Shift / C** |
| 주석 도구 모음 표시 전환 | **Space** |
| 크기 조절 중 영역 이동 | 왼쪽 버튼을 누른 채 **Space** |
| 취소 / 영역 다시 선택 | **Esc / 오른쪽 클릭** |

트레이 메뉴에는 전체 화면·3초 지연·최근 영역·좌표 및 크기 지정 캡처와 화이트보드도 있습니다.
선택 영역 아래 도구 모음의 주석 도구를 누르면 같은 화면 위에서 편집합니다.
캡처·주석 완료 후 자동으로 플로팅합니다. 설정 → 캡처에서 자동 플로팅을 끌 수 있으며, Ctrl+T는 이 설정과 관계없이 플로팅합니다.
기본 캡처는 GPU Desktop Duplication을 사용하고, 사용할 수 없으면 GDI 호환 방식으로 전환합니다.
설정 → 캡처에서 GPU 우선 사용을 끄면 GDI 방식부터 사용합니다. Windows 캡처 도구를 호출하지 않습니다.
새 캡처 전에 열려 있던 편집창은 캡처에서 제외합니다. 캡처 취소 시 복원하고, 완료 시 작업 표시줄로 최소화해 편집 내용을 보존합니다.

## 주석

- 사각형, 타원, 선, 화살표, 꺾은선, 펜, 형광펜, 문자, 순서 번호
- 모자이크, 흐림, 지우개, 자르기
- 색상·선 굵기·채움·알파, 문자 서체·굵게·기울임·회전
- 주석 선택 후 이동·크기 변경, 문자 더블클릭 수정
- **Ctrl+Z** 실행 취소, **Ctrl+Y** 다시 실행, **Ctrl+Shift+Z** 모든 편집 초기화
- **Shift**로 정사각형·원·각도 제한, **Tab**으로 선·화살표 전환
- PNG/JPEG/BMP 저장, PNG 클립보드 형식으로 투명도 보존

화이트보드에서는 도구 모음이 처음에 숨겨져 있습니다. **Space**로 표시하며, 종료는 도구 모음의 닫기 버튼을 사용합니다.

## 화면 고정

| 동작 | 조작 |
|---|---|
| 클립보드 붙여넣기 / 최근 닫은 이미지 복구 | **F3**, 트레이 가운데 클릭 |
| 현재 그룹 전체 숨기기 / 표시 | **Shift+F3** |
| 커서 아래 이미지 클릭 통과 | **Ctrl+Alt+F3** |
| 다음 이미지 그룹 | **Ctrl+Shift+F3** |
| 이동 / 다른 창에 정렬 | 드래그 / **Shift+드래그** |
| 여러 이미지 선택 / 전체 선택 | **Ctrl+클릭 / Ctrl+A** |
| 배율 변경 | 휠, **+ / -**, 테두리 드래그 |
| 불투명도 변경 | **Ctrl+휠**, **Ctrl + (+ / -)** |
| 배율·불투명도 100% | 가운데 클릭 |
| 시계 / 반시계 90° 회전 | **1 / 2** |
| 좌우 / 상하 반전 | **3 / 4** |
| 같은 이미지 위치에서 주석 편집 | **Space** |
| 접기 / 펼치기 | **Shift+더블클릭** |
| 복구 가능한 닫기 | **Esc**, **Ctrl+W**, 더블클릭 |
| 이미지 완전히 제거 | **Shift+Esc** |
| 클립보드로 이미지 교체 | **Ctrl+V** |
| 원문 텍스트 복사 | **Ctrl+Shift+C** |
| 설정 열기 | **Ctrl+Shift+P** |

이미지·GIF·텍스트·기본 HTML/RTF 서식·HEX/RGB 색상·이미지 파일을 붙일 수 있습니다.
같은 이미지 파일을 다시 붙이면 파일 경로를 텍스트 이미지로 표시합니다(설정에서 끌 수 있음).
닫은 이미지 복구 개수 기본값은 1개이며, 전체 숨기기는 이 개수에 영향을 주지 않습니다.
클릭 통과를 끄려면 이미지가 없는 곳에서 해당 단축키를 누르거나 트레이의 표시·클릭 통과 해제를 사용하세요.

GIF는 우클릭 메뉴에서 재생·일시정지, 이전·다음 프레임, 속도를 조절합니다.
현재 프레임은 PNG로 저장하며, 원본 GIF와 변환·재생 상태는 다음 실행을 위해 별도로 보관합니다.
그룹 관리에서 생성·이름 변경·전환, 선택 이미지 이동, `.chacha` 그룹 파일 내보내기·가져오기를 지원합니다.
그룹 파일은 Chacha 형식이며 Snipaste 그룹 파일과 호환되지 않습니다.

## 설정과 보관

설정은 단축키 / 캡처 / 저장·기록 / 고정·시작 탭으로 구성됩니다.
단축키 입력란을 선택하고 실제 키를 누르면 조합을 인식합니다. **지우기**로 기능별 단축키를 해제할 수 있고, 전부 비워 두어도 됩니다.
설정 중에는 앱 전역 단축키를 잠시 해제하므로 F1/F3도 캡처를 시작하지 않고 입력할 수 있습니다.
Snipaste 같은 다른 캡처 앱과 함께 실행할 때는 겹치는 단축키를 지우거나 다른 키로 지정하세요. 등록에 실패한 키는 대시보드에 표시합니다.
5개 전역 단축키, 커서 포함, UI 요소 감지, 다른 창 활성화 시 캡처 취소, 자동 저장,
두 저장 폴더, 기록 개수(1–200), 닫은 이미지 보관(0–100), HTML과 경로 붙여넣기를 변경합니다.
Windows 로그인 시 실행도 설정에서 선택할 수 있습니다.

- 설정·기록·고정 이미지: `%LOCALAPPDATA%\ChachaCapture`
- 기본 저장·빠른 저장: Windows 사진 폴더의 `Chacha Capture`
- 오류 로그: `%LOCALAPPDATA%\ChachaCapture\error.log`
- 업데이트 확인·다운로드에만 GitHub를 사용합니다. 캡처 이미지 업로드나 계정 로그인 기능은 없습니다.

## 명령행

실행 중인 앱에도 명령을 전달합니다. 경로와 공백이 있는 문자는 따옴표로 감싸세요.

```powershell
.\ChachaCapture.exe snip
.\ChachaCapture.exe snip --full -o clipboard
.\ChachaCapture.exe snip --area 100 100 640 480 --delay 1.5 -o pin
.\ChachaCapture.exe snip --last -o quick-save
.\ChachaCapture.exe snip --active-window -o "C:\Pictures\window.png"
.\ChachaCapture.exe paste --plain "메모 내용" --pos 100 100
.\ChachaCapture.exe paste --files "C:\Pictures\sample.gif"
.\ChachaCapture.exe whiteboard
.\ChachaCapture.exe switch-group "기본 그룹"
.\ChachaCapture.exe open-preferences
.\ChachaCapture.exe exit
```

추가 명령: `toggle-images`, `show-images`, `hide-images`, `create-group`, `switch-groups`,
`show-group-manager`, `empty-group`, `toggle-click-through`, `no-click-through`, `show-tray-menu`.
캡처 옵션: `--full`, `--last`, `--area`, `--size`, `--active-window`, `--active-screen`, `--custom`, `--delay`.
출력: `pin`, `clipboard`, `quick-save`, `file-dialog`, `printer`, `success`, 파일 경로.
실행 옵션: `--tray`, `--open FILE`, `--data-dir DIRECTORY`.

## 호환 범위

v1.2.0은 실제 키 입력·선택적 단축키, 캡처 호환성, 자동 플로팅을 개선한 버전입니다. **Snipaste 전체 제품과 완전한 1:1 일치를 보증하는 버전은 아닙니다.**
UI는 Chacha 한국어 테마이며, 세부 차이는 [기능 대조표](https://github.com/youkdonghun/chacha/blob/main/docs/PARITY.md)에 명시했습니다.
OCR·바코드·가상 데스크톱 연동 같은 PRO 기능, 모든 HTML/CSS의 동일 렌더링, Snipaste 설정·그룹 파일 호환은 포함하지 않습니다.
UI 요소 감지는 대상 앱의 접근성 정보 제공 여부에 따라 달라집니다.
보호된 영상·보안 데스크톱·HDR, 실제 혼합 DPI 다중 모니터는 별도 검증이 필요합니다.

## 빌드

```powershell
.\build.ps1 -Test
```

Windows 내장 Framework64 C# 5 컴파일러를 사용합니다. .NET SDK와 NuGet 설치는 필요하지 않습니다.
결과: `dist\ChachaCapture.exe`, `dist\ChachaCapture-1.2.0-win-x64.zip`, SHA-256, 자체 검사 결과.
[검증 기록](docs/VALIDATION.md)과 [변경점](docs/RELEASE-v1.2.0.md)을 참고하세요.

MIT License. 작성자: youkdonghun.
