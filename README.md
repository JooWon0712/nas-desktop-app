# NAS Desktop

NAS 웹 관리 화면을 전용 창에서 사용하는 **Windows 10/11 x64 전용** 앱입니다. 직접 HTTPS 주소와 Synology QuickConnect를 지원합니다. macOS·Linux는 지원하지 않습니다.

**[Windows ZIP 다운로드](https://github.com/JooWon0712/nas-desktop-app/releases/download/v0.15.0-preview.1/NasDesktop-windows-x64.zip)** · [릴리스·체크섬](https://github.com/JooWon0712/nas-desktop-app/releases/tag/v0.15.0-preview.1)

## 주요 기능

- 작업 표시줄 최소화와 기본 창 X로 앱 종료
- 읽기 전용 주소창과 주소 기록 모달을 통한 NAS 연결
- NAS 이름 지정·수정, 주소 추가·삭제, 최근 30개 기록
- QuickConnect 자동 주소 전환, 라이트·다크·시스템 모드와 6가지 색상
- Windows 계정으로 주소·기록 보호, NAS별 로그인 프로필과 보안 설정

## 빠른 시작

**필수:** .NET Framework 4.8 이상, [WebView2 Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section), 정상 인증서가 적용된 NAS HTTPS 주소.

1. ZIP을 **폴더 전체로 압축 해제**하고 `NasDesktop.exe`를 실행합니다. DLL 3개도 같은 폴더에 두세요.
2. **주소 기록 → 새 주소 추가**에서 접속 방식과 주소를 입력하고 **추가**를 누릅니다. 별칭은 추가 후 **기존 NAS 이름 → 이름 저장**에서 지정합니다.
3. 목록에서 NAS를 골라 **선택 NAS 연결**을 누르고, NAS 웹 화면에서 로그인합니다.
4. 다른 NAS로 연결하려면 상단 주소창을 클릭하거나 **Ctrl+L**로 주소 기록을 엽니다.

주소 예: HTTPS 모드의 `https://nas.example.com/`, QuickConnect 모드의 `my-nas` 또는 `quickconnect.to/my-nas`.

**상단 URL은 항상 읽기 전용**입니다. 기본 창의 **X는 앱과 관련 팝업을 종료**하며, 최소화하면 작업 표시줄에서 계속 실행합니다. 시스템 트레이에는 등록하지 않습니다.

업데이트할 때는 앱을 종료한 뒤 새 ZIP의 네 파일을 **기존 실행 폴더에 덮어써 EXE의 전체 경로를 유지**하세요. [업데이트 안내](docs/USAGE.md#업데이트)를 참고하세요.

## 사용 전 확인

- 제조사 공식 앱이 아닌 **미서명 프리뷰**입니다. 실제 제조사별 2FA·업로드·패키지 전체 호환성 검증은 끝나지 않았습니다.
- HTTPS 인증서 검증을 유지하며, HTTP 직접 접속이나 인증서 경고 무시는 지원하지 않습니다.
- 주소·이름을 DPAPI로 보호하고 NAS 식별 기준별로 프로필을 나눕니다. 같은 Windows 계정에서 실행되는 악성 프로그램까지 차단하지는 않습니다. [보안·개인정보 상세](docs/SECURITY.md)를 확인하세요.

## 상세 문서

| 문서 | 내용 |
| --- | --- |
| [설치와 사용 안내](docs/USAGE.md) | 주소 예시, 단축키, 테마, 지원 범위, 문제 해결, 업데이트·초기화 |
| [보안과 개인정보](docs/SECURITY.md) | 인증서·이동 제한, QuickConnect 검증, 로그인·세션, 데이터 저장, 체크섬 |
| [개발과 검증](docs/DEVELOPMENT.md) | 빌드·패키징·테스트, 검증 결과와 한계, CI |

문제 제보는 [Issues](https://github.com/JooWon0712/nas-desktop-app/issues)를 이용해 주세요. 공개 글과 스크린샷에서 개인 NAS 주소·토큰·로그인 데이터를 제거하세요.
