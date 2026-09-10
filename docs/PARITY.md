# Snipaste 기능 대조표 · v1.2.0

기준: 2026-09-10에 확인한 Snipaste Windows 무료판 공식 문서.
'구현'은 대응 기능이 있다는 뜻이며, 원본과 모든 픽셀·예외 상황이 동일하다는 뜻은 아닙니다.

| 영역 | 구현한 동작 | 차이·제약 |
|---|---|---|
| 캡처 | F1, 트레이, 지연·전체·최근·정확한 영역 | 모든 마우스 동작 사용자 지정은 미지원 |
| 감지 | 창, UI Automation 요소, Tab 전환, 휠 계층 탐색 | 접근성 제공자에 의존; 응답 지연 시 창 영역으로 대체 |
| 픽셀 제어 | 영역 이동·크기, WASD, 확대경, RGB/HEX | HDR 색 관리 미검증 |
| 완료 | Enter/Ctrl+C 복사 후 자동 플로팅, Ctrl+T 플로팅, 저장·빠른 저장·인쇄 | 자동 플로팅을 설정에서 해제 가능; 취소된 저장은 성공 기록에서 제외 |
| 캡처 방식 | GPU Desktop Duplication / GDI 호환 방식, 불투명 화면 픽셀 | OS 캡처 감지·보안·보호 화면을 우회하지 않음 |
| 단축키 설정 | 실제 키 조합 인식, 기능별 지우기·미지정, 설정 중 전역 키 일시 해제 | Windows 키 조합은 미지원 |
| 기록 | 쉼표·마침표, 원래 화면 좌표, 개수 제한 | 저장 영역을 현재 고정 배경 위에 재구성; 당시 전체 바탕화면은 보관하지 않음 |
| 주석 | 도형·선·화살표·꺾은선·펜·형광펜·문자·번호·모자이크·흐림·지우개·자르기 | UI 요소 오른쪽 클릭으로 자동 주석 생성은 미구현 |
| 주석 수정 | 이동·크기·문자 회전·색상·알파·굵기·채움·undo/redo/초기화 | 모든 도구별 미세 설정과 동일하지 않음 |
| 붙여넣기 | 이미지·텍스트·RGB/HEX·HTML/RTF·파일·TGA | HTML은 기본 텍스트 서식; CSS 레이아웃·웹 이미지 미지원 |
| 고정 창 | 이동·회전·반전·배율·불투명도·접기·정렬·클릭 통과·드롭·원문 복사 | 화면 밖 복원 시 현재 모니터 안으로 보정 |
| 닫기·숨기기 | F3 복구, Shift+F3 전체 숨기기, Shift+Esc 제거 | 기본 보관 1개, 최대 100개 |
| 고정 창 편집 | 원래 위치·배율에서 도구 모음으로 편집 후 반영 | 편집은 현 프레임을 정적 이미지로 전환; 세션 간 주석 객체 재편집 미지원 |
| GIF | 재생·정지·프레임·속도·회전·반전·복원 | 애니메이션 주석·변환 GIF 인코딩 미지원 |
| 그룹 | 생성·전환·이름 변경·선택 이동·내보내기/가져오기 | Chacha 전용 파일, Snipaste 그룹·설정 파일 불호환 |
| 화이트보드 | 화면 배경 위 주석, Space 도구 모음, Esc 무시 | PRO 투명 화이트보드 미지원 |
| 명령행 | 주요 snip/paste/group/whiteboard/트레이 | --hold, --block, 그림자, 파일명 변수, 출력 조합·exec 미지원 |
| PRO | 일부 직접 재편집·다중 선택 포함 | OCR, QR/바코드, 가상 데스크톱 연동, 핫코너 등 미구현 |
| UI | 한국어 작은 도구 모음, 대시보드·그룹·탭 설정 | 원본 테마·레이아웃·아이콘과 동일하지 않음 |

공식 참고 문서:

- [Getting Started](https://github.com/Snipaste/feedback/wiki/Getting-Started)
- [Key Bindings](https://github.com/Snipaste/feedback/wiki/Key-Bindings)
- [Advanced Tips](https://github.com/Snipaste/feedback/wiki/Advanced-Tips)
- [Command Line Options](https://github.com/Snipaste/feedback/wiki/Command-Line-Options)
- [PRO](https://github.com/Snipaste/feedback/wiki/PRO)
