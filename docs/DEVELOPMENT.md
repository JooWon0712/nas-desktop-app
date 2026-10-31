# 개발과 검증

[← README](../README.md) · [사용법](USAGE.md) · [보안](SECURITY.md)

## 빌드 환경

구현은 **C# 5 / WinForms / .NET Framework 4.8**, WebView2 SDK **1.0.4314.35**를 사용합니다. Windows x64의 PowerShell에서 실행합니다. 앱 실행과 웹 화면을 포함한 기능 검사에는 Microsoft Edge WebView2 Evergreen Runtime이 필요합니다.

[build.ps1](../build.ps1)은 Windows의 .NET Framework C# 컴파일러를 사용하고, 처음에는 NuGet에서 SDK를 받아 `.packages/`에 보관합니다. 공개 ZIP에는 WebView2 Runtime을 포함하지 않습니다.

## 빌드와 패키징

프로젝트 루트에서 실행합니다.

```powershell
# 공개용 빌드와 ZIP 생성
.\build.ps1
.\package.ps1
```

기본 빌드 결과는 `dist/NasDesktop/`, 실행 파일은 `dist/NasDesktop/NasDesktop.exe`입니다. 공유 ZIP은 `dist/NasDesktop-windows-x64.zip`입니다. 빌드의 다른 출력 경로는 `-OutputDirectory`로 지정합니다.

```powershell
.\build.ps1 -OutputDirectory 'dist\NasDesktop-dev'
.\package.ps1 -AppDirectory 'dist\NasDesktop-dev' -OutputFile 'dist\NasDesktop-dev.zip'
```

개인 PC용으로만 `build.ps1 -IncludeLocalSettings`를 사용하면 프로젝트의 `connection.local.json`이 있을 때 실행 폴더에 복사합니다. 기본 빌드는 이 설정을 포함하지 않으며 이전 출력에 남은 설정도 제거합니다. 설정 형식은 [connection.example.json](../connection.example.json)을 참고하세요.

공개 배포는 실행 폴더 전체를 압축하지 않고 **반드시 [package.ps1](../package.ps1)**로 생성하세요. 이 스크립트는 개인 개발 빌드에서도 아래 네 파일만 선별하며 사용자 설정·NAS 이름·주소 기록·프로필을 포함하지 않습니다.

```text
NasDesktop.exe
Microsoft.Web.WebView2.Core.dll
Microsoft.Web.WebView2.WinForms.dll
WebView2Loader.dll
```

`connection.local.json`, 주소 기록 파일, `.packages/`, `dist/`, `test-output/`은 Git에서 제외합니다. 로컬 데이터와 공유 시 주의할 내용은 [보안 문서](SECURITY.md)를 참고하세요.

## 로컬 검증

```powershell
# 격리된 기능 검사와 패키징 검사
.\test.ps1
.\tools\test-packaging.ps1
```

`v0.13.0-preview.1` 배포 전 로컬 환경에서 **기능 268개·패키징 11개 검사**를 통과했습니다. [기능 검사](../test.ps1)는 `test-output/`의 격리된 프로필과 가상 HTTPS 페이지를 사용합니다.

- 주소 읽기 전용·모달 입력/연결·중복 모달 차단·잘못된 주소 거부·저장 실패 시 원본 유지
- 이름·주소 기록 저장/삭제, 쿠키·웹 저장소, 팝업, 트레이 숨김/복원, 파일 다운로드
- QuickConnect 후보 검증·ID/도메인 경계·늦은 응답 폐기, 격리된 renderer/browser 충돌 후 복구
- 96/144 DPI, 라이트·다크·최소 창 배치
- 공개 ZIP 네 파일 허용 목록과 개인 데이터 제외

QNAP·ASUSTOR·TrueNAS 관련 검사는 제조사별 경로·포트를 흉내 낸 **합성 화면** 기준입니다. 별도 읽기 전용 확인에서는 한 Synology NAS의 QuickConnect 자동 전환과 DSM 로그인 화면 표시를 확인했으며 로그인은 수행하지 않았습니다. 실제 장비의 2FA·업로드·다중 파일 전송·모든 패키지와 모든 QuickConnect 지역/릴레이 조합을 검증한 것은 아닙니다. Windows 종료 분기 검사도 실제 재부팅 검사는 아닙니다.

[패키징 검사](../tools/test-packaging.ps1)는 임시 junction을 만들고, 기능 검사는 격리된 다운로드 파일 쓰기와 웹 프로세스 충돌을 재현하므로 제한된 실행 환경에서는 권한 문제가 날 수 있습니다.

## CI의 검증 범위

[GitHub Actions 워크플로](../.github/workflows/build.yml)는 Windows **빌드·ZIP 생성·아티팩트 보관**을 수행합니다. 현재 워크플로는 위 기능 검사 전체를 자동 실행하지 않습니다. 로컬 검사 통과 결과와 CI 빌드 성공은 검증 범위가 다릅니다.

기능 검사 통과는 독립 보안 감사·침투 테스트·보안 인증을 의미하지 않습니다. 보호 동작과 적용 범위는 [보안 문서](SECURITY.md)에서 확인할 수 있습니다.

## 라이선스

현재 저장소에는 별도의 `LICENSE` 파일이 포함되어 있지 않습니다.
