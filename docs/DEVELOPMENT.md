# 개발과 검증

[← README](../README.md) · [사용법](USAGE.md) · [보안](SECURITY.md)

## 빌드 환경

구현은 **C# 5 / WinForms / .NET Framework 4.8**, WebView2 SDK **1.0.4314.35**를 사용합니다. Windows x64의 PowerShell에서 실행합니다. 앱 실행과 웹 화면을 포함한 기능 검사에는 Microsoft Edge WebView2 Evergreen Runtime이 필요합니다.

[build.ps1](../build.ps1)은 Windows의 .NET Framework C# 컴파일러를 사용하고, 처음에는 NuGet에서 SDK를 받습니다. `.packages/`에는 현재 고정한 SDK의 DLL 3개만 캐시합니다. 다운로드는 [공식 NuGet 카탈로그](https://api.nuget.org/v3/catalog0/data/2026.10.23.06.50.10/microsoft.web.webview2.1.0.4314.35.json)의 SHA-512 고정값으로 확인하고, 사용하는 DLL 3개도 매 빌드마다 고정 해시와 대조합니다. SDK 버전을 바꿀 때는 패키지와 DLL 해시를 함께 갱신해야 합니다. 공개 ZIP에는 WebView2 Runtime을 포함하지 않습니다.

## 빌드와 패키징

프로젝트 루트에서 실행합니다.

```powershell
# 공개용 빌드와 ZIP 생성
.\build.ps1
.\package.ps1
```

기본 빌드 결과는 `dist/NasDesktop/`, 실행 파일은 `dist/NasDesktop/NasDesktop.exe`입니다. 공유 ZIP은 `dist/NasDesktop-windows-x64.zip`이며 `package.ps1`이 같은 폴더에 `SHA256SUMS.txt`를 생성합니다. 빌드의 다른 출력 경로는 `-OutputDirectory`로 지정합니다.

```powershell
.\build.ps1 -OutputDirectory 'dist\NasDesktop-dev'
.\package.ps1 -AppDirectory 'dist\NasDesktop-dev' -OutputFile 'dist\NasDesktop-dev.zip'
```

개인 PC용으로만 `build.ps1 -IncludeLocalSettings`를 사용하면 프로젝트의 `connection.local.json`이 있을 때 실행 폴더에 복사합니다. 이 개발용 원본은 평문일 수 있으며 앱이 자동으로 보호 형식으로 바꾸지 않습니다. 기본 빌드는 이 설정을 포함하지 않으며 이전 출력에 남은 설정도 제거합니다. 설정 형식은 [connection.example.json](../connection.example.json)을 참고하세요.

공개 배포는 실행 폴더 전체를 압축하지 않고 **반드시 [package.ps1](../package.ps1)**로 생성하세요. 이 작업본부터 생성하는 ZIP은 아래 여섯 파일만 선별하며 사용자 설정·NAS 이름·주소 기록·프로필을 포함하지 않습니다. 라이선스 고지 두 파일은 실행 폴더의 사본 대신 프로젝트 루트의 정본에서 가져옵니다. 완성된 ZIP을 다시 열어 파일 목록과 내용 해시를 검사합니다.

```text
NasDesktop.exe
Microsoft.Web.WebView2.Core.dll
Microsoft.Web.WebView2.WinForms.dll
WebView2Loader.dll
LICENSE
THIRD_PARTY_NOTICES.txt
```

이미 공개된 **v0.15.0-preview.1 ZIP은 실행 파일 네 개 구성**을 유지합니다. 기존 ZIP·체크섬·출처 증명은 바꾸지 않으며, 라이선스 고지 두 개는 이 작업본의 다음 패키지부터 포함합니다.

`connection.local.json`, 주소 기록 파일, `.packages/`, `dist/`, `test-output/`은 Git에서 제외합니다. 로컬 데이터와 공유 시 주의할 내용은 [보안 문서](SECURITY.md)를 참고하세요.

### 생성 파일 자동 정리

[BuildArtifacts.ps1](../tools/BuildArtifacts.ps1)은 빌드·기능 검사·패키징 검사의 임시 작업을 `test-output/.generated/` 아래에 격리합니다. 소유 표식과 사용 중 잠금이 있는 작업 폴더만 관리하며, 성공·실패 모두 종료 시 정리합니다. 비정상 종료로 남은 폴더는 다음 실행에서 회수하고, 사용 중이거나 잠긴 폴더는 건너뛴 뒤 다음 실행에서 다시 시도합니다.

NuGet 원본·임시 압축 파일·아이콘·검사용 실행 파일과 프로필은 보관하지 않습니다. SDK 교체 시에도 관리 표식이 있는 이전 SDK 캐시만 정리합니다. 최신 검사 결과인 `test-output/results.txt`, `stdout.txt`, `stderr.txt`는 남깁니다.

기본 빌드는 `dist/NasDesktop/`, 기본 패키지는 같은 ZIP 경로를 갱신합니다. 사용자가 지정한 출력 폴더·릴리스 ZIP, 표식 없는 기존 `test-output` 파일, 개인 설정·수동 복구 백업과 `%LOCALAPPDATA%/NasDesk/`의 로그인 데이터는 자동 삭제하지 않습니다.

## 로컬 검증

```powershell
# 격리된 기능 검사, 패키징·정리 검사
.\test.ps1
.\tools\test-packaging.ps1
.\tools\test-cleanup.ps1
```

현재 작업본에서 **기능 331개·패키징 20개·자동 정리 21개 검사**를 통과했습니다. 기능 검사 종료 후 임시 작업 폴더가 남지 않는 것도 확인했습니다. 공개 프리뷰 배포 버전은 **v0.15.0-preview.1**입니다.

[기능 검사](../test.ps1)는 `test-output/.generated/`의 관리되는 임시 작업 폴더에 테스트 EXE·프로필·검사 데이터를 두고 가상 HTTPS 페이지를 사용합니다. 종료 후 임시 파일을 정리하고 최신 결과 파일만 남기며, 실제 사용자 설정·프로필이나 NAS 로그인을 사용하지 않습니다.

- 주소 읽기 전용·모달 입력/연결·중복 모달 차단·잘못된 주소 거부·저장 실패 시 원본 유지
- 이름·주소 기록 저장/삭제, 쿠키·웹 저장소, 팝업, 최소화와 창 종료, 파일 다운로드
- QuickConnect 후보 검증·ID/도메인 경계·늦은 응답 폐기, 격리된 renderer/browser 충돌 후 복구
- 96/144 DPI, 라이트·다크·최소 창 배치
- 공개 ZIP 여섯 파일 허용 목록·라이선스 고지와 개인 데이터 제외

QNAP·ASUSTOR·TrueNAS 관련 검사는 제조사별 경로·포트를 흉내 낸 **합성 화면** 기준입니다. 별도 읽기 전용 확인에서는 한 Synology NAS의 QuickConnect 자동 전환과 DSM 로그인 화면 표시를 확인했으며 로그인은 수행하지 않았습니다. 실제 장비의 2FA·업로드·다중 파일 전송·모든 패키지와 모든 QuickConnect 지역/릴레이 조합을 검증한 것은 아닙니다. Windows 종료 분기 검사도 실제 재부팅 검사는 아닙니다.

[패키징 검사](../tools/test-packaging.ps1)는 임시 junction을 만들고, 기능 검사는 격리된 다운로드 파일 쓰기와 웹 프로세스 충돌을 재현하므로 제한된 실행 환경에서는 권한 문제가 날 수 있습니다.

[정리 검사](../tools/test-cleanup.ps1)는 격리된 작업 폴더에서 생성 파일 회수와 사용 중 폴더·관리 대상 밖의 파일 보존을 검증합니다.

0.14 보안 작업에서는 격리된 fixture로 SDK DLL·패키지 변조 거부, 잘못된 다운로드 정리, 정상 캐시 복구, ZIP 내용·중복 항목·체크섬 확인을 추가 검증했습니다. 첫 SDK 다운로드부터 검증·추출·빌드하는 경로와 기존 체크섬을 교체하는 반복 패키징도 확인했습니다.

### 창 종료와 OS 등록

앱은 실행 모드와 테스트 모두 `NotifyIcon` 객체를 만들거나 OS 트레이 아이콘을 등록하지 않습니다. 기본 창을 닫으면 관련 팝업도 종료하고, 최소화하면 작업 표시줄에서 계속 실행합니다. 시각적 프로브도 `testMode: true`와 격리된 데이터를 사용합니다.

트레이를 사용하던 이전 버전의 이력은 Windows에 남을 수 있으며 앱에서 자동 정리하지 않습니다. 사용자별 정리·진단 도구는 공개 앱과 저장소의 기능에 포함하지 않습니다.

## CI의 검증 범위

[GitHub Actions 워크플로](../.github/workflows/build.yml)는 Windows **빌드·ZIP 생성·체크섬·아티팩트 보관**을 수행하며 사용하는 Actions를 전체 커밋 SHA로 고정합니다. 현재 워크플로는 위 기능 검사 전체를 자동 실행하지 않습니다.

공개 저장소의 기본 브랜치에서 수행한 빌드는 별도 최소 권한 작업으로 **해당 CI ZIP과 ZIP 내부 EXE의 GitHub 출처 증명**을 생성합니다. 체크섬을 검사한 ZIP에서 EXE를 추출해 두 파일을 하나의 증명에 기록하고, CI 안에서도 저장소·서명 워크플로·소스 커밋·브랜치와 GitHub 호스팅 실행 환경을 확인합니다. 두 파일 모두 검증해야 증명 bundle을 내보냅니다. PR 빌드에는 증명용 권한을 부여하지 않습니다.

공개 릴리스에는 이 CI 산출물과 체크섬·증명 bundle을 사용하며 로컬 재빌드 파일로 대체하지 않습니다. 실제 배포 전 ZIP과 EXE를 같은 bundle로 다시 검증합니다. 로컬에서 별도로 만든 파일과 이전 릴리스에는 이 구성이 적용됐다고 보장하지 않습니다. 출처 증명은 Authenticode 코드 서명이 아니며 현재 EXE는 미서명입니다.

기능 검사 통과는 독립 보안 감사·침투 테스트·보안 인증을 의미하지 않습니다. 보호 동작과 적용 범위는 [보안 문서](SECURITY.md)에서 확인할 수 있습니다.

## 라이선스

프로젝트 자체 소스는 [MIT 라이선스](../LICENSE)를 사용합니다. 누구나 사용·복제·수정·배포할 수 있고 상업적 이용도 허용합니다. 복제본이나 소프트웨어의 주요 부분을 재배포할 때 저작권·허가 고지를 유지해야 합니다. 수정한 소스의 공개를 요구하는 라이선스는 아니며, 소프트웨어는 보증 없이 제공됩니다.

WebView2 SDK DLL·로더 등 외부 구성요소의 권리와 조건은 MIT로 바뀌지 않습니다. [THIRD_PARTY_NOTICES.txt](../THIRD_PARTY_NOTICES.txt)의 해당 고지를 함께 보존하세요. .NET Framework·WebView2 Runtime·접속하는 NAS 소프트웨어도 각 제공자의 조건을 따릅니다. 오픈 소스 라이선스를 추가하는 것이 무료 코드 서명 서비스의 심사 승인을 뜻하지는 않습니다.
