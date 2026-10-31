# nas-desktop-app 개발 지침

- Windows 전용 C# 5 / WinForms / .NET Framework 4.8 + WebView2 기반 범용 NAS 웹 앱이다. 실행 파일은 `dist/NasDesktop/NasDesktop.exe`다.
- 기본 창의 X는 앱과 관련 팝업을 종료한다. 팝업의 X는 해당 팝업만 닫는다. 최소화하면 작업 표시줄에서 계속 실행한다. Windows 종료를 취소하지 않는다.
- 시스템 트레이 아이콘·메뉴·숨김 기능을 추가하지 않는다. 이전 버전의 Windows 트레이 이력도 앱에서 자동 정리하지 않는다. 사용자 요청 없이 TrySuspend, 강제 로그아웃, GPU 플래그, 자동 시작, 서비스를 추가하지 않는다.
- 인증서 검증, 웹 보안, 출처 제한을 우회하지 않는다. 웹 콘텐츠에 네이티브 호스트 브리지를 노출하지 않는다.
- NAS 주소·프로필·계정·쿠키는 개인 데이터다. `connection.local.json`, `.packages`, `dist`, `test-output`은 Git에서 제외한다. 배포·업로드는 별도 사용자 요청이 있어야 한다.
- 빌드: `./build.ps1`. 동작 검증: `./test.ps1`. 테스트는 로컬 가상 HTTPS 페이지를 사용한다. 실제 NAS 로그인이나 파일 변경을 자동화하지 않는다.
- 빌드·기능 검사·패키징 검사의 임시 파일은 `tools/BuildArtifacts.ps1`을 통해 `test-output/.generated`의 소유 표식·사용 중 잠금이 있는 작업 폴더에 격리하고, 실패하더라도 종료 시 정리한다. 다음 실행은 소유 표식이 있는 유휴 잔여 폴더만 회수하며, 활성·잠긴 폴더는 건너뛰고 나중에 다시 시도한다. 최신 `test-output/results.txt`, `stdout.txt`, `stderr.txt`는 보존한다. 정리 검증은 `./tools/test-cleanup.ps1`이다.
- SDK 캐시는 현재 고정한 DLL 3개만 남기고 NuGet 원본·임시 압축 파일·아이콘·컴파일 임시 파일은 정리한다. 이전 SDK도 관리 표식이 있을 때만 자동 정리한다. 기본 `dist/NasDesktop`과 패키지 ZIP 경로는 갱신하되, 사용자 지정 출력·릴리스 ZIP을 이름으로 추측해 삭제하지 않는다. 표식 없는 `test-output` 자료·개인 설정·수동 복구 백업과 `%LOCALAPPDATA%/NasDesk`는 자동 정리 대상이 아니다.
- 일반·시각적 테스트 프로브도 `testMode: true`와 격리된 데이터를 사용한다. 화면이 필요하면 테스트 창의 Opacity만 조정한다. 앱과 프로브에서 NotifyIcon 생성이나 OS 트레이 등록을 도입하지 않는다.
- 기본 빌드는 개인 주소 설정을 포함하지 않는다. 개인 PC용 빌드에서만 `-IncludeLocalSettings`를 사용한다. 공개 배포는 반드시 `./package.ps1`의 네 파일 허용 목록 ZIP을 사용하고 원본 실행 폴더 전체를 업로드하지 않는다.
- 제조사 공통 직접 HTTPS 주소와 선택 기능인 Synology QuickConnect ID·URL을 지원하는 프리뷰다. QuickConnect 전환은 입력 ID의 공식 HTTPS 메타데이터에 포함된 정확한 주소와 추가 기기 일치 확인을 통과한 경우만 허용한다. 전체 quickconnect 하위 도메인을 신뢰하거나 HTTP·인증서 검증 우회를 추가하지 않는다.
- 연결 주소를 변경하면 이전 ID의 비동기 응답·허용 주소·진행 중 검증을 폐기한다. 제조사별 패키지/2FA/파일 업로드 호환성을 실제 기기 검증 없이 완료로 설명하지 않는다.
