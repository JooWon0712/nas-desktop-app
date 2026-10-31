# synology-nas-desktop-app

**Synology NAS Desktop**은 Synology DSM을 전용 Windows 창에서 여는 비공식 앱입니다. X로 창을 닫으면 시스템 트레이에서 계속 실행합니다. 현재는 **지원 조건을 충족하는 사용자가 시험 사용하는 프리뷰**입니다.

## 다운로드와 실행

**[Windows x64 프리뷰 ZIP 다운로드](https://github.com/JooWon0712/synology-nas-desktop-app/releases/download/v0.5.0-preview.1/SynologyNasDesktop-windows-x64.zip)** · [릴리스 안내와 SHA-256](https://github.com/JooWon0712/synology-nas-desktop-app/releases/tag/v0.5.0-preview.1)

1. ZIP을 내려받아 **폴더 전체를 압축 해제**합니다. GitHub의 `Code → Download ZIP`은 소스 코드입니다.
2. `SynologyNasDesktop.exe`를 실행합니다. 같은 폴더의 DLL 세 개도 함께 있어야 합니다.
3. **QuickConnect ID**, `quickconnect.to/my-nas` 같은 주소, 또는 NAS의 HTTPS 주소를 입력합니다. ID만 입력해도 됩니다. `quickconnect.cn` 주소도 입력 형식을 지원합니다.
4. DSM 로그인 화면에서 직접 로그인합니다. Chrome/Edge의 로그인 상태와는 별도입니다.

Windows 10/11 **x64**, **.NET Framework 4.8 이상**, **Microsoft Edge WebView2 Evergreen Runtime**이 필요합니다. WebView2가 없으면 [Microsoft 공식 설치 페이지](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section)에서 설치하세요. 별도 관리자 권한이나 .NET SDK는 앱 실행에 필요하지 않습니다. 현재 실행 파일은 코드 서명되지 않은 프리뷰입니다.

## 창과 주소

- **X / Alt+F4:** 창을 숨기고 트레이에서 계속 실행합니다. 최소화 버튼은 작업 표시줄 최소화입니다.
- **트레이 더블클릭 / 창 열기:** 창을 복원합니다. Windows가 아이콘을 숨기면 알림 영역의 `^`에서 찾으세요.
- **트레이 → 완전 종료:** 앱과 관련 창을 종료합니다. Windows 로그오프·종료도 허용합니다.
- **주소 수정 ON:** 주소를 편집한 뒤 **연결 / Enter**로 저장·접속합니다. 연결하면 수정 모드가 꺼집니다. **OFF / Esc**는 미저장 수정을 취소합니다.
- **홈 / 새로 고침:** 저장된 NAS 홈 또는 현재 화면을 다시 엽니다.
- **테마 / 트레이 → 색상 테마:** 차콜 블루·그래파이트 바이올렛·웜 앰버·네이비 민트 중 선택합니다. 즉시 적용되고 다음 실행에도 유지됩니다. NAS 웹 화면 자체의 테마는 DSM에서 설정합니다.
- **다운로드:** DSM의 파일 받기를 사용합니다. 기본 다운로드 목록은 **Ctrl+J**로 열 수 있습니다.

상단에는 홈·새로 고침·주소 입력줄, 연결 상태와 테마 선택을 표시합니다. 같은 NAS의 새 창은 별도 앱 창으로 열고 로그인 저장 공간을 공유합니다. 이미 실행 중인 앱을 다시 실행하면 기존 창을 엽니다. 시작 프로그램이나 Windows 서비스로 자동 등록하지 않습니다.

## 지원 조건과 한계

- **QuickConnect 자동 주소 전환을 지원합니다.** Synology 포털이 연결 경로를 선택하면, 앱은 입력한 ID로 공식 HTTPS 연결 정보를 조회하고 반환된 정확한 접속 주소인지와 기기 식별값의 일치를 추가 확인한 뒤 이동합니다. 입력한 ID·주소는 유지되고 확인된 접속 주소는 현재 연결의 메모리에만 보관됩니다. 주소를 변경하면 이전 연결의 확인 결과를 폐기합니다.
- **HTTPS와 정상 인증서는 계속 필요합니다.** QuickConnect의 HTTP 시작 주소는 HTTPS로 바꾸지만 NAS로의 HTTP 연결이나 인증서 경고 무시는 허용하지 않습니다. NAS가 오프라인이거나 QuickConnect/DSM 접근이 꺼져 있거나 유효한 HTTPS 연결 경로가 없으면 접속할 수 없습니다.
- **관련 없는 주소는 계속 제한합니다.** 같은 `quickconnect.to` 하위 주소라는 이유만으로 다른 NAS를 허용하지 않습니다. 별도 포트 패키지·외부 인증 흐름은 입력 ID에 결합된 연결 정보로 확인되지 않으면 추가 호환 처리가 필요할 수 있습니다.
- 실제 DSM의 **2단계 인증, 파일 업로드, 다중 파일 전송, 패키지별 호환성은 검증 완료가 아닙니다.** 이 표현은 해당 기능의 실제 계정·장비에서 성공 여부를 아직 충분히 확인하지 않았다는 뜻입니다. 모든 DSM 버전과 네트워크에서 동작한다고 보장하지 않습니다.
- GPU·보조 웹 프로세스 오류의 자동 복구를 방해하지 않습니다. 웹 화면이 중단되면 **다시 연결**로 재로드하고, 메인 웹 엔진이 종료되면 웹 컨트롤을 새로 만들어 같은 프로필을 사용합니다. 충돌 직전의 미저장 웹 작업은 복구를 보장하지 않습니다.
- 숨겨도 웹 엔진은 CPU·메모리 등 자원을 사용합니다. 강제 일시 정지나 GPU 설정 변경은 적용하지 않습니다. PC 절전·네트워크 단절·DSM 세션 만료가 화면 기능에 영향을 줄 수 있습니다.
- 파일 동기화 클라이언트가 아니므로 앱을 켜두는 것만으로 PC 파일을 자동 백업하지 않습니다. NAS의 서버 작업은 NAS에서 처리합니다.

## 개인정보와 로컬 설정

- 접속 주소: `%LOCALAPPDATA%\NasDesk\connection.json`
- 앱 색상: `%LOCALAPPDATA%\NasDesk\appearance.json` — 테마 이름만 저장하며 색상 변경 시 NAS 화면이나 로그인 상태를 초기화하지 않습니다.
- 로그인 쿠키·캐시·웹 저장소: `%LOCALAPPDATA%\NasDesk\WebView2`
- 앱은 계정·암호를 별도 설정 파일에 저장하지 않고 암호 자동 저장도 끕니다. DSM의 로그인 유지 선택과 세션 만료 정책을 따릅니다.
- 개인 `connection.local.json`, 프로필, 빌드·테스트 산출물은 Git에서 제외합니다. **공개 ZIP은 EXE와 필수 DLL 세 개만 포함**하며 개인 주소가 없는 상태로 시작합니다.

## 업데이트

1. 트레이에서 **완전 종료**를 선택합니다. X는 종료가 아닙니다.
2. 새 ZIP을 새 폴더에 전부 압축 해제하고 실행합니다.
3. 사용자별 주소와 웹 프로필은 위의 로컬 데이터 폴더에 남습니다. 로그인 유지 여부는 DSM 정책을 따릅니다.

자동 업데이트 기능은 없습니다. [릴리스 페이지](https://github.com/JooWon0712/synology-nas-desktop-app/releases)에서 버전과 변경 사항을 확인하세요.

## 개발·빌드·검사

C# 5 / WinForms / .NET Framework 4.8 / Microsoft.Web.WebView2 **1.0.4314.35**. PowerShell에서 실행합니다.

```powershell
./build.ps1
./package.ps1
./test.ps1
./tools/test-packaging.ps1
```

`build.ps1`은 Windows의 .NET Framework C# 컴파일러를 사용하고, 처음에는 공식 NuGet에서 WebView2 SDK를 받습니다. 결과는 `dist/SynologyNasDesktop/`, 공유용 ZIP은 `dist/SynologyNasDesktop-windows-x64.zip`입니다. 실행용 WebView2 Evergreen Runtime은 번들하지 않습니다.

기본 빌드는 개인 주소 설정을 포함하지 않으며 이전 빌드 폴더에 남은 `connection.local.json`도 제외합니다. 개인 개발용으로만 `./build.ps1 -IncludeLocalSettings`를 사용하세요. 이때도 `package.ps1`은 네 개의 실행 파일만 선별하므로 개인 설정을 공유 ZIP에 포함하지 않습니다. `connection.example.json`은 설정 형식 예시입니다.

자동 검사는 실제 NAS에 접근하지 않는 가상 HTTPS 페이지와 `test-output/`의 격리된 프로필을 사용합니다. 주소 편집·취소·저장, QuickConnect 입력 형식·메타데이터 후보 검증·ID/도메인 경계·자동 전환 재개·주소 변경 후 늦은 응답 폐기, 트레이 숨김·복원·종료, 쿠키·저장소, 팝업, DPI 배치, 파일 다운로드와 목록 열기를 검사합니다. 해당 테스트 프로필에서만 renderer/browser 충돌을 일으켜 재연결도 확인합니다. 다운로드 파일 쓰기를 검증하므로 샌드박스의 파일 접근 제한 아래에서는 별도 권한 오류가 날 수 있습니다. 실제 사용자 프로필이나 다른 앱 프로세스는 대상으로 삼지 않습니다.

별도의 읽기 전용 실측에서 한 NAS의 QuickConnect ID로 시작해 직접 HTTPS 주소로 자동 전환되고 DSM 로그인 화면이 표시되는 것을 확인했습니다. 로그인은 수행하지 않았습니다. 이 결과가 모든 지역·릴레이·NAS 설정의 성공을 의미하지는 않습니다. 기기 식별값 비교는 Synology HTTPS 연결 정보에 더하는 일치 검사이며 암호학적 소유권 서명으로 설명하지 않습니다.

주소줄·상태 배지·트레이 메뉴·테마 선택 메뉴는 100%·150% 배율과 최소 창 크기에서 렌더링을 확인했습니다. 테마 저장과 변경 시 웹 문서·쿠키·미저장 주소 편집이 유지되는지도 격리된 테스트로 확인합니다. Windows 종료는 종료 이유별 분기 검사이며 실제 재부팅은 하지 않았습니다. 실제 트레이 아이콘을 직접 누르는 검사, NAS 재로그인·파일 업로드·패키지 호환성은 별도 확인이 필요합니다. GitHub Actions는 Windows 빌드와 공유 ZIP 생성 결과를 보관합니다.

구현 근거: [Synology QuickConnect 백서](https://kb.synology.com/en-us/WP/Synology_QuickConnect_White_Paper/4), [Synology 포털 연결 라이브러리](https://portal.quickconnect.to/connect_lib.da3fae9c5d057ef58d3a.bundle.js), [WebView2 WinForms](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/winforms), [프로세스 오류와 복구](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/process-related-events), [웹 프로필](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder), [WebView2 보안](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security).
