# synology-nas-desktop-app 개발 지침

- Windows 전용 C# 5 / WinForms / .NET Framework 4.8 + WebView2 앱이다. 실행 파일은 `dist/SynologyNasDesktop/SynologyNasDesktop.exe`다.
- X는 기본 창과 관련 팝업을 트레이로 숨긴다. 명시적 종료·Windows 종료를 취소하지 않는다.
- 트레이 상태에 WebView를 유지한다. 사용자 요청 없이 TrySuspend, 강제 로그아웃, GPU 플래그, 자동 시작, 서비스를 추가하지 않는다.
- 인증서 검증, 웹 보안, 출처 제한을 우회하지 않는다. 웹 콘텐츠에 네이티브 호스트 브리지를 노출하지 않는다.
- NAS 주소·프로필·계정·쿠키는 개인 데이터다. `connection.local.json`, `.packages`, `dist`, `test-output`은 Git에서 제외한다. 배포·업로드는 별도 사용자 요청이 있어야 한다.
- 빌드: `./build.ps1`. 동작 검증: `./test.ps1`. 테스트는 로컬 가상 HTTPS 페이지를 사용한다. 실제 NAS 로그인이나 파일 변경을 자동화하지 않는다.
- 기본 빌드는 개인 주소 설정을 포함하지 않는다. 개인 PC용 빌드에서만 `-IncludeLocalSettings`를 사용한다. 공개 배포는 반드시 `./package.ps1`의 네 파일 허용 목록 ZIP을 사용하고 원본 실행 폴더 전체를 업로드하지 않는다.
- 현재는 직접 HTTPS 접속을 지원하는 프리뷰다. QuickConnect 자동 주소 전환·자체 서명 인증서 우회·전체 DSM 패키지/2FA/파일 업로드 호환성을 검증 완료로 설명하지 않는다.
