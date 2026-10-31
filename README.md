# NAS Desktop

**NAS의 웹 관리 화면을 전용 Windows 창에서 사용하는 비공식 데스크톱 앱입니다.**

프로젝트 이름은 `nas-desktop-app`입니다. 제조사 공통 **HTTPS 관리 주소**와 **Synology QuickConnect**를 지원하며, 기본 창의 X를 눌러도 시스템 트레이에서 계속 실행합니다. 현재 배포는 **프리뷰**이며 제조사·패키지별 호환성을 확인하며 사용해야 합니다.

**[Windows x64 ZIP 다운로드](https://github.com/JooWon0712/nas-desktop-app/releases/download/v0.13.0-preview.1/NasDesktop-windows-x64.zip)** · [릴리스 안내·SHA-256](https://github.com/JooWon0712/nas-desktop-app/releases/tag/v0.13.0-preview.1) · [보안 설계와 한계](#보안-설계와-한계)

Synology·QNAP·ASUSTOR·TrueNAS 등 각 제조사의 공식 클라이언트가 아니며, 해당 업체와의 제휴나 보증을 의미하지 않습니다. 파일 동기화·자동 백업 기능은 제공하지 않습니다.

## 주요 기능

- NAS 웹 화면과 같은 NAS의 팝업을 별도 앱 창으로 표시합니다.
- 기본 창에서 **X / Alt+F4 → 트레이 숨김**, 트레이 더블클릭으로 복원합니다.
- 상단 주소는 **항상 읽기 전용**이며, 주소 입력·선택·연결은 **주소 기록 모달**에서만 수행합니다.
- NAS 이름 지정·수정, 주소 추가·삭제와 최근 30개 기록을 지원합니다.
- Synology QuickConnect ID·URL을 입력하면 확인된 HTTPS 연결 경로로 자동 전환합니다.
- 라이트·다크·Windows 시스템 모드와 6가지 색상 테마를 제공합니다.
- 연결 실패 안내, 재시도, 웹 엔진 오류 후 재연결을 지원합니다.

## 설치와 첫 연결

### 실행 환경

| 항목 | 조건 |
| --- | --- |
| 운영체제 | Windows 10/11 x64 |
| .NET | .NET Framework 4.8 이상 |
| 웹 엔진 | Microsoft Edge WebView2 Evergreen Runtime |
| NAS | 접근 가능한 HTTPS 웹 관리 주소와 정상적으로 검증되는 인증서 |
| 실행 권한 | 일반 사용자 권한으로 실행 가능. 앱이 관리자 승격을 요청하지 않음 |

WebView2 Runtime이 없으면 [Microsoft 공식 다운로드 페이지](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section)에서 설치하세요. 앱 ZIP에는 Runtime을 포함하지 않습니다. Edge 브라우저와 WebView2 Runtime의 배포 방식은 [Microsoft 문서](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)에서 확인할 수 있습니다.

### 실행하기

1. 위의 **Windows x64 ZIP**을 내려받아 폴더 전체를 압축 해제합니다. GitHub의 `Code → Download ZIP`은 소스 코드입니다.
2. 압축을 해제한 폴더에서 **`NasDesktop.exe`**를 실행합니다.
3. 처음 실행하면 **주소 기록** 모달이 열립니다. 기존 설정이 있으면 저장된 NAS를 열며, 상단 주소창을 클릭해 모달을 열 수 있습니다.
4. **새 주소 추가**에서 접속 방식·주소와 선택 사항인 NAS 이름을 입력하고 **추가**를 누릅니다.
5. 목록에서 NAS를 선택한 뒤 **선택 NAS 연결**을 누릅니다.
6. 표시된 NAS 웹 화면에서 직접 로그인합니다. Chrome·Edge 브라우저의 로그인 상태를 가져오지 않습니다.

공개 ZIP에는 아래 네 파일이 들어 있습니다. EXE와 DLL을 같은 폴더에 두세요.

```text
NasDesktop.exe
Microsoft.Web.WebView2.Core.dll
Microsoft.Web.WebView2.WinForms.dll
WebView2Loader.dll
```

### 주소 입력 예시

| 방식 | 입력 예시 | 처리 |
| --- | --- | --- |
| HTTPS 주소 | `https://nas.example.com:443/` | 지정한 HTTPS 관리 화면 열기 |
| HTTPS 주소 | `nas.local:443`, `nas` | 생략한 스킴을 HTTPS로 보완. 내부 호스트명을 Synology에 보내지 않음 |
| HTTPS 주소 | `https://192.0.2.10/`, `https://[2001:db8::10]:443/` | IPv4·IPv6 주소 지원 |
| QuickConnect | `my-nas`, `quickconnect.to/my-nas` | Synology QuickConnect ID의 연결 경로 확인 |
| QuickConnect | `quickconnect.cn/my-nas` | 입력한 QuickConnect 지역 도메인 사용 |

예시는 주소 형식을 설명하기 위한 가상 값입니다. 실제 접속 가능한 주소로 바꿔 입력하세요. 직접 HTTP 접속과 주소의 `user:password@host` 형식은 허용하지 않습니다.

## 사용 방법

### 창과 주소

| 조작 | 동작 |
| --- | --- |
| 주소창·테두리·주소 기록 버튼 클릭 | 주소 기록 모달 열기 |
| `Ctrl+L` | 웹 화면에서도 주소 기록 모달 열기 |
| 주소에 포커스를 둔 상태의 `Enter` / `Alt+↓` | 주소 기록 모달 열기 |
| NAS 홈 / 새로 고침 | 저장된 NAS 홈 / 현재 웹 화면 다시 열기 |
| 기본 창의 X / `Alt+F4` | 기본 창과 관련 팝업을 트레이로 숨김 |
| 팝업 자체의 X | 해당 팝업을 닫으며 기본 창은 유지 |
| 트레이 더블클릭 / NAS 창 열기 | 숨겨진 창 복원 |
| 트레이 → 완전 종료 | 앱과 관련 창 종료 |
| 최소화 버튼 | 작업 표시줄로 최소화 |

**상단 URL은 표시·복사용입니다.** 직접 타이핑하거나 붙여넣어 연결을 변경할 수 없습니다. NAS가 연 팝업 창의 주소도 읽기 전용이며, 팝업에서는 주소 기록을 열 수 없습니다. 앱을 다시 실행하면 새 인스턴스 대신 기존 창을 엽니다.

### 주소 기록 모달

- **새 주소 추가:** 이름·주소·방식을 입력하고 **추가**를 누르면 저장하고 해당 항목을 선택합니다. 이 동작만으로 접속을 바꾸지는 않습니다.
- **기존 NAS 이름:** 목록의 항목을 선택해 이름을 고치고 **이름 저장**을 누릅니다. 이름은 60자 이내이며, 빈 이름은 호스트명으로 표시합니다.
- **선택 NAS 연결:** 선택한 주소를 저장하고 바로 접속합니다. 설정 저장에 실패하면 모달과 입력을 유지하고 오류를 표시합니다.
- **삭제:** 선택한 기록만 삭제합니다. 현재 접속 주소와 로그인 데이터는 삭제하지 않습니다.
- **닫기 / Esc:** 연결을 선택하지 않고 닫으면 현재 접속을 유지합니다. 이미 **추가·이름 저장·삭제**로 저장한 기록 변경은 유지합니다.

기록은 주소와 접속 방식 기준으로 중복을 정리하고 최근 순서로 최대 30개를 보관합니다. 재연결해도 지정한 이름은 유지하며, 앱을 열기만 해서 삭제한 기록을 다시 추가하지 않습니다. 현재 활성 연결은 하나입니다.

### 테마·다운로드·연결 실패

**테마** 또는 트레이 메뉴에서 라이트·다크·시스템 모드와 차콜 블루·그래파이트 바이올렛·웜 앰버·포레스트 그린·세이지 올리브·네이비 민트 색상을 고를 수 있습니다. 설정은 다음 실행에도 유지합니다. 앱 테마는 창·상단·메뉴에 적용하며 NAS 웹 화면의 밝기는 NAS에서 설정합니다.

파일 받기는 NAS 웹 화면의 다운로드 기능을 사용하며, 기본 다운로드 목록은 **Ctrl+J**로 엽니다. 브라우저 기본 스크롤바의 두께와 대비를 보완하지만 제조사 자체 스크롤 UI에는 다르게 적용될 수 있습니다.

연결 실패 시 앱 테마에 맞는 안내 화면에서 **다시 연결 / 주소 선택**을 제공합니다. 로그인·권한 관련 HTTP 401/403/407 응답은 NAS의 안내 화면을 유지합니다. 웹 엔진을 재생성할 때도 같은 프로필을 사용하지만, 충돌 직전의 미저장 웹 작업 복구는 보장하지 않습니다.

## 보안 설계와 한계

이 절은 **현재 코드의 보호 동작과 적용 범위**를 설명합니다. 기능 검사 통과는 독립 보안 감사·침투 테스트·보안 인증을 의미하지 않습니다. 앱의 보안은 Windows·WebView2 Runtime·접속하는 NAS와 인증서 신뢰 환경에도 의존합니다.

### HTTPS와 주소 이동 제한

| 보호 항목 | 현재 동작과 범위 |
| --- | --- |
| HTTPS 접속 | 사용자 접속 주소와 QuickConnect 확인 요청에 HTTPS를 사용합니다. 공식 QuickConnect HTTP 입력을 받는 경우에도 요청 전에 HTTPS 주소로 정규화합니다. |
| 인증서 검증 | WebView2와 .NET의 기본 HTTPS 검증을 유지합니다. 인증서 오류 무시·예외 승인·검증 비활성화 기능을 제공하지 않으며, 별도 인증서 고정도 구현하지 않습니다. |
| 직접 NAS 연결 | 앱 내 **최상위 페이지 이동과 새 창**을 설정한 HTTPS 호스트·포트로 제한합니다. 같은 출처 안의 경로는 제한하지 않습니다. |
| QuickConnect 이동 | 설정 ID의 공식 연결 정보와 기기 식별값을 확인한 추가 HTTPS 출처만 현재 연결의 메모리 허용 목록에 넣습니다. `quickconnect` 하위 도메인 전체를 신뢰하지 않습니다. |
| 주소 변경 후 처리 | 이전 연결의 확인 결과·진행 중 검증을 폐기해 늦게 도착한 응답이 새 연결을 덮어쓰지 않도록 합니다. |

신뢰되지 않은 자체 서명 인증서, 만료·이름 불일치 등의 인증서 오류가 있으면 연결하지 못할 수 있습니다. 인증서 신뢰 여부는 Windows·WebView2 등의 기본 검증에 따릅니다. 앱에서 경고를 우회하는 대신 NAS 관리자에게 정상 인증서와 관리 주소를 확인하세요.

**이 제한은 모든 외부 통신을 차단하는 방화벽이 아닙니다.** 페이지가 불러오는 이미지·스크립트·API 요청·하위 프레임 전체를 앱이 같은 허용 목록으로 필터링하지는 않습니다. 웹 콘텐츠의 CORS·CSP·혼합 콘텐츠 등의 처리는 기본 웹 엔진 정책에 따르며, 앱에서 해당 보안을 끄는 옵션을 추가하지 않습니다.

구현: [주소 검증](src/ConnectionAddress.cs) · [탐색 허용 정책](src/ConnectionSession.cs) · [WebView 이벤트 처리](src/MainWindow.cs).

### 웹 콘텐츠와 Windows 기능 사이의 경계

- 웹 페이지의 **네이티브 호스트 객체 접근과 WebMessage 통로를 비활성화**합니다. 페이지에 파일 시스템·셸 실행용 앱 브리지를 제공하지 않습니다.
- 설정한 NAS 출처 이외의 웹 권한 요청은 거부합니다. NAS 출처의 권한은 WebView2 기본 동작과 기존 권한 결정을 따르며, 모든 권한을 일괄 거부하거나 항상 새로 묻도록 강제하지는 않습니다.
- 웹 페이지의 외부 앱 URI 스킴 실행을 취소합니다. 사용자 동작으로 열린 미허용 HTTPS **새 창**은 확인 후 운영체제 기본 브라우저로 넘길 수 있습니다. 일반 외부 페이지 이동은 앱 안에서 차단합니다.
- 문서 생성 시 기본 스크롤바를 꾸미는 고정 스크립트를 넣습니다. 이 스크립트는 로그인·폼 데이터 수집이나 네이티브 명령 실행 통로를 제공하지 않습니다.
- 실행 매니페스트는 `asInvoker`이며 관리자 승격을 요청하지 않습니다. 일반 사용자 권한으로 실행하는 것이 WebView2의 [권장 보안 원칙](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)에 맞습니다.

앱은 다운로드 파일에 대한 자체 악성코드 검사나 NAS의 취약점 검사를 제공하지 않습니다. 신뢰하는 NAS 주소와 파일을 사용하고 Windows·NAS·Runtime의 보안 업데이트를 유지하세요.

### QuickConnect 확인과 외부 서비스 통신

QuickConnect 모드에서는 Synology 포털이 선택한 경로를 확인하기 위해 다음 절차를 사용합니다.

1. 입력 ID와 서비스 경로로 공식 QuickConnect 연결 정보를 조회합니다.
2. 응답에 명시된 HTTPS 호스트·포트인지 확인합니다.
3. 후보 NAS의 응답과 연결 정보의 기기 식별값이 일치하는지 확인한 뒤 이동을 허용합니다.

기기 식별값의 MD5 비교는 프로토콜의 일치 확인이며, 비밀키 서명·기기 소유권·로그인 권한을 증명하지 않습니다. 확인은 Synology 서비스·DNS·HTTPS 신뢰 체계에 의존합니다. 확인용 요청에는 브라우저 프로필의 쿠키나 NAS 로그인 자격증명을 가져와 사용하지 않고, 자동 리다이렉트를 끄며 시간·응답 크기를 제한합니다. 구현은 [QuickConnectResolver.cs](src/QuickConnectResolver.cs)에서 확인할 수 있습니다.

**HTTPS 주소 모드는 내부 호스트명을 QuickConnect ID로 해석해 Synology에 보내지 않습니다.** QuickConnect 모드를 선택하면 ID·서비스 경로가 Synology에 전달되고, 확인 과정에서 반환된 NAS 후보 주소에도 요청할 수 있습니다.

앱 소스에는 별도 사용 분석·텔레메트리 수집 전송 기능을 구현하지 않았습니다. 다만 NAS 웹 페이지가 불러오는 외부 서비스, Synology QuickConnect, WebView2 Runtime의 네트워크 동작까지 없다는 뜻은 아닙니다.

### 로그인과 세션

- 로그인은 NAS가 제공하는 웹 화면에서 처리합니다. 앱이 계정·암호 입력 폼을 별도로 만들거나 로그인 API를 대신 호출하지 않습니다.
- 비밀번호 자동 저장을 끕니다. 이는 **로그인 쿠키·토큰·웹 저장소나 일반 폼 데이터가 전혀 저장되지 않는다는 뜻은 아닙니다.**
- X로 숨기기, 모달 닫기, 테마 변경, 주소 기록 삭제는 로그아웃이나 브라우저 데이터 삭제를 수행하지 않습니다. 로그아웃은 NAS 웹 화면에서 하세요.
- 재실행 후 로그인 유지 여부는 NAS의 로그인 유지 선택·세션 만료 정책과 WebView2 저장 상태에 따릅니다. 항상 로그인 상태가 유지된다고 보장하지 않습니다.
- **모든 NAS와 관련 팝업은 하나의 지속 WebView2 프로필을 공유합니다.** NAS별 독립 프로필·계정 격리는 제공하지 않습니다. 쿠키는 같은 호스트의 포트마다 분리되는 저장소가 아니므로, 포트만 다르면 로그인 데이터가 완전히 분리된다고 가정하지 마세요. [WebView2 프로필 저장소](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder), [쿠키의 포트 경계](https://www.rfc-editor.org/rfc/rfc6265#section-8.5)를 참고하세요.

## 개인정보와 로컬 데이터

사용자 데이터의 기본 위치는 **`%LOCALAPPDATA%\NasDesk`**입니다. 예전 프로젝트 이름에서 업데이트하더라도 이 경로를 유지합니다.

| 위치 | 저장하는 내용 |
| --- | --- |
| `connection.json` | 현재 접속 주소와 방식 |
| `address-history.json` | NAS 이름·전체 URL·방식, 최대 30개 |
| `appearance.json` | 색상 테마와 화면 모드 |
| `WebView2/` | Runtime이 관리하는 쿠키·캐시·웹 저장소·권한 등 브라우저 프로필 데이터 |

**설정과 주소 기록 JSON은 앱에서 별도로 암호화하지 않는 평문 파일입니다.** 경로·쿼리·프래그먼트를 포함한 전체 URL을 저장하므로, 붙여넣은 주소의 일회용 토큰이나 민감한 값도 남을 수 있습니다. `user:password@host`는 거부하지만 쿼리에 들어 있는 비밀 값을 모두 탐지·제거하지는 않습니다.

브라우저 프로필은 Runtime이 관리하며 앱이 별도의 암호화·PC 사용자 분리 장치를 추가하지 않습니다. 데이터 폴더나 스크린샷을 공유할 때 주소·이름·토큰·쿠키 등 민감한 내용을 확인하세요. 이 앱의 주소 기록은 웹 페이지 전체 방문 이력을 수집하는 기능과는 구분되지만, WebView2 자체 프로필에는 브라우저 데이터가 남을 수 있습니다.

`connection.local.json`, 주소 기록 파일, `.packages/`, `dist/`, `test-output/`은 Git에서 제외합니다. **공개 ZIP은 EXE와 필수 DLL 3개만 선별**하며 사용자 설정·이름·기록·프로필을 포함하지 않습니다. 개인 실행 폴더 전체를 공유하지 마세요.

## 지원 범위와 알려진 한계

| 항목 | 현재 범위 |
| --- | --- |
| NAS 제조사 | HTTPS 웹 관리 화면을 여는 범용 방식. Synology·QNAP·ASUSTOR·TrueNAS 장비 전체의 동작을 보증하지 않음 |
| 클라우드 ID | Synology QuickConnect 지원. myQNAPcloud·ASUSTOR EZ-Connect 등은 직접 HTTPS 주소 사용 |
| 동시 연결 | 활성 연결 하나. 기록에서 다른 NAS로 전환 가능 |
| 별도 포트·외부 SSO | 호스트·포트 이동 제한으로 추가 호환 처리가 필요할 수 있음 |
| HTTP·인증서 예외 | 직접 HTTP 접속과 앱 내 인증서 오류 무시 미지원 |
| 2FA·업로드·패키지 | 실제 제조사별 전체 호환성 검증 미완료 |
| 실행 중 자원 | 트레이에 숨겨도 웹 엔진이 CPU·메모리 등을 사용하며 강제 일시 정지하지 않음 |
| 업데이트 | 앱 자동 업데이트 없음. Runtime Evergreen의 업데이트와는 별개 |

현재 EXE는 **코드 서명되지 않은 프리뷰**입니다. [릴리스 첨부 파일](https://github.com/JooWon0712/nas-desktop-app/releases)에서 내려받고 아래 방법으로 ZIP의 일치 여부를 확인할 수 있습니다.

ZIP과 `SHA256SUMS.txt`를 같은 릴리스에서 함께 내려받아 같은 폴더에 두고, 그 폴더에서 실행하세요.

```powershell
$expectedHash = ((Get-Content -LiteralPath .\SHA256SUMS.txt -Raw).Trim() -split '\s+')[0]
$actualHash = (Get-FileHash -LiteralPath .\NasDesktop-windows-x64.zip -Algorithm SHA256).Hash
$actualHash -eq $expectedHash
```

`True`가 나오면 해시가 일치합니다. SHA-256 일치 확인은 파일 무결성 확인 수단이며 게시자 코드 서명이나 보안 감사를 대신하지 않습니다.

## 자주 겪는 문제

| 증상 | 확인할 내용 |
| --- | --- |
| DLL을 찾을 수 없음 | EXE만 꺼내지 않았는지, ZIP 네 파일을 함께 압축 해제했는지 확인 |
| WebView2를 준비하지 못함 | 공식 Evergreen Runtime 설치 상태와 사용자 데이터 폴더 쓰기 권한 확인 |
| 연결 거부·시간 초과 | NAS 전원·HTTPS 관리 포트·주소·PC 네트워크·필요한 VPN 확인 |
| 인증서 오류 | 주소와 인증서 이름·유효기간·신뢰 설정을 NAS 관리자와 확인 |
| 다른 주소로 이동이 차단됨 | 최종 HTTPS 관리 주소를 입력했는지 확인. 별도 포트 패키지·외부 인증은 호환성 확인 필요 |
| 주소창에 입력되지 않음 | 정상 동작. 주소창 클릭 또는 Ctrl+L로 모달을 열어 주소 추가·연결 |
| X를 눌러도 계속 실행됨 | 정상 동작. 트레이에서 완전 종료 선택 |
| 재실행 후 로그인이 풀림 | NAS 세션 정책·로그인 유지 설정·프로필 저장 상태 확인 |

## 업데이트·제거·초기화

### 업데이트

1. 트레이에서 **완전 종료**를 선택합니다. 기본 창의 X는 종료가 아닙니다.
2. 새 릴리스 ZIP을 새 폴더에 전부 압축 해제하고 실행합니다.
3. 사용자 데이터는 `%LOCALAPPDATA%\NasDesk`에 남으므로 주소·이름·테마·프로필을 이어 사용합니다. 로그인 유지 여부는 NAS 정책에 따릅니다.

이 앱에는 자동 업데이트 기능이 없습니다. WebView2 Evergreen Runtime은 별도 업데이트 체계를 사용합니다. [Microsoft의 배포·업데이트 설명](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/evergreen-vs-fixed-version)을 참고하세요.

### 제거와 데이터 초기화

트레이에서 완전 종료한 뒤 압축 해제한 실행 폴더를 제거하면 됩니다. 시작 프로그램이나 Windows 서비스를 자동 등록하지 않습니다. 실행 폴더를 지워도 사용자 데이터는 남습니다.

초기화가 필요하면 완전 종료 후 파일 탐색기에서 `%LOCALAPPDATA%\NasDesk`를 열어 필요한 항목을 백업하고 삭제하거나 이름을 바꿉니다.

- 주소 기록만 초기화: `address-history.json`
- 현재 연결 선택 초기화: `connection.json`
- 색상·화면 모드 초기화: `appearance.json`
- 앱 내 로그인·사이트 데이터 초기화: `WebView2` 폴더. 저장된 로그인 상태·캐시·권한 등이 함께 사라집니다.

로컬 프로필을 지우는 것과 NAS 서버에서 세션을 취소하는 것은 다릅니다. 서버 측 세션 종료가 필요하면 NAS의 로그아웃·세션 관리 기능을 사용하세요.

개인 개발 빌드의 실행 폴더에 `connection.local.json`이 있으면 `connection.json`을 지워도 그 주소를 초기값으로 사용할 수 있습니다. 공개 ZIP에는 이 파일이 없습니다.

## 개발과 검증

구현은 **C# 5 / WinForms / .NET Framework 4.8**, WebView2 SDK **1.0.4314.35**를 사용합니다. PowerShell에서 실행합니다.

```powershell
# 공개용 빌드와 ZIP 생성
.\build.ps1
.\package.ps1

# 격리된 기능 검사와 패키징 검사
.\test.ps1
.\tools\test-packaging.ps1
```

`build.ps1`은 Windows의 .NET Framework C# 컴파일러를 사용하고, 처음에는 NuGet에서 SDK를 받습니다. 결과는 `dist/NasDesktop/`, 공유 ZIP은 `dist/NasDesktop-windows-x64.zip`입니다. 다른 출력 경로는 `-OutputDirectory`로 지정합니다.

개인 PC용으로만 `build.ps1 -IncludeLocalSettings`를 사용하면 프로젝트의 `connection.local.json`을 실행 폴더에 복사합니다. 기본 빌드는 이 설정을 포함하지 않으며 이전 출력에 남은 설정도 제거합니다. 공개 배포는 실행 폴더 전체를 압축하지 않고 **반드시 `package.ps1`**로 생성하세요. 설정 형식은 [connection.example.json](connection.example.json)을 참고하세요.

### 확인한 것과 확인하지 않은 것

`v0.13.0-preview.1` 배포 전 로컬 환경에서 **기능 268개·패키징 11개 검사**를 통과했습니다. 테스트는 `test-output/`의 격리된 프로필과 가상 HTTPS 페이지를 사용합니다.

- 주소 읽기 전용·모달 입력/연결·중복 모달 차단·잘못된 주소 거부·저장 실패 시 원본 유지
- 이름·주소 기록 저장/삭제, 쿠키·웹 저장소, 팝업, 트레이 숨김/복원, 파일 다운로드
- QuickConnect 후보 검증·ID/도메인 경계·늦은 응답 폐기, 격리된 renderer/browser 충돌 후 복구
- 96/144 DPI, 라이트·다크·최소 창 배치
- 공개 ZIP 네 파일 허용 목록과 개인 데이터 제외

QNAP·ASUSTOR·TrueNAS 관련 검사는 제조사별 경로·포트를 흉내 낸 **합성 화면** 기준입니다. 별도 읽기 전용 확인에서는 한 Synology NAS의 QuickConnect 자동 전환과 DSM 로그인 화면 표시를 확인했으며 로그인은 수행하지 않았습니다. 실제 장비의 2FA·업로드·다중 파일 전송·모든 패키지와 모든 QuickConnect 지역/릴레이 조합을 검증한 것은 아닙니다. Windows 종료 분기 검사도 실제 재부팅 검사는 아닙니다.

[GitHub Actions](.github/workflows/build.yml)는 Windows **빌드·ZIP 생성·아티팩트 보관**을 수행합니다. 현재 워크플로는 위 기능 검사 전체를 자동 실행하지 않습니다. 패키징 검사는 임시 junction을 만들고, 기능 검사는 격리된 다운로드 파일 쓰기와 웹 프로세스 충돌을 재현하므로 제한된 실행 환경에서는 권한 문제가 날 수 있습니다.

## 문제 제보와 라이선스

[Issues](https://github.com/JooWon0712/nas-desktop-app/issues)에 앱 버전·Windows/WebView2 버전·재현 단계를 적어 주세요. NAS 주소·QuickConnect ID·사용자 이름·토큰·쿠키·프로필·실제 파일은 공개 글이나 스크린샷에서 제거하세요. 민감한 취약점은 공개하지 말고 저장소 관리자와 비공개 공유 경로를 먼저 확인하세요.

현재 저장소에는 별도의 `LICENSE` 파일이 포함되어 있지 않습니다.
