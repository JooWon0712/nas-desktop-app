# synology-nas-desktop-app

**Synology NAS Desktop**은 Synology DSM 웹 화면을 전용 Windows 창에서 여는 비공식 데스크톱 앱입니다. 브라우저에서 사용하는 HTTPS 주소에 접속하며, 창의 **X를 누르면 시스템 트레이에서 계속 실행**합니다.

짙은 네이비와 민트색의 전용 메뉴, 서버 아이콘, 연결 상태 표시, 어두운 제목 표시줄을 적용했습니다. DSM 화면은 넓은 작업 영역에 그대로 표시합니다. Windows의 기본 창 이동·크기 조절·최소화·최대화 기능을 유지합니다.

## 실행

`dist/SynologyNasDesktop/SynologyNasDesktop.exe`를 실행하세요. **SynologyNasDesktop 폴더 전체를 함께 보관**해야 합니다. EXE와 같은 폴더의 WebView2 DLL 세 개가 필요합니다. GitHub Actions의 Windows build가 성공하면 실행 폴더를 아티팩트로 내려받을 수 있습니다.

Windows 10/11 x64, .NET Framework 4.8 이상, Microsoft Edge WebView2 Evergreen Runtime이 필요합니다. 개발 PC에서는 설치된 WebView2 Runtime으로 빌드 및 로컬 화면 검사를 마쳤습니다. 별도 관리자 권한이나 .NET SDK는 실행에 필요하지 않습니다.

1. 처음 실행하면 NAS의 HTTPS 접속 주소를 입력합니다. 이후 DSM 화면에서 직접 로그인합니다. 기존 Edge/Chrome 로그인과는 별도입니다.
2. X 또는 Alt+F4를 누르면 창을 숨깁니다. 최소화 버튼은 일반 작업 표시줄 최소화입니다.
3. 알림 영역의 Synology NAS Desktop 아이콘을 더블클릭하거나 우클릭 → **NAS 창 열기**로 복원합니다. Windows가 아이콘을 숨긴 경우 `^`를 눌러 확인하세요.
4. 앱을 끝내려면 트레이 우클릭 → **완전 종료**를 누릅니다. Windows 로그오프·종료 때도 앱 종료를 허용합니다.

접속 주소는 상단의 주소 입력란에 표시됩니다. 기본 상태에서는 선택·복사만 할 수 있습니다. **주소 수정** 스위치를 켜고 주소를 입력한 다음 **연결** 또는 Enter를 누르면 저장·접속하고 수정 모드가 꺼집니다. 스위치를 끄거나 Esc를 누르면 저장하지 않은 수정은 취소하고 기존 주소를 복원합니다. 별도의 숨기기 버튼 없이 X로 트레이에 숨깁니다.

같은 주소의 새 창은 별도 앱 창으로 열고 로그인 저장 공간을 공유합니다. 상단의 **다운로드**는 WebView2 기본 다운로드 목록을 엽니다. **브라우저**는 **Windows에 설정된 기본 브라우저**에서 저장된 NAS 홈 주소를 엽니다. 해당 브라우저의 로그인 상태는 앱과 별도입니다.

앱은 시작 프로그램이나 Windows 서비스로 등록되지 않습니다. PC를 다시 켠 후에는 직접 실행하세요. 이미 실행 중인 앱을 다시 실행하면 기존 창을 엽니다.

## 로그인과 데이터

- 로그인 쿠키·캐시·웹 저장소: `%LOCALAPPDATA%\NasDesk\WebView2`
- 사용자가 바꾼 접속 주소: `%LOCALAPPDATA%\NasDesk\connection.json`
- 첫 연결 주소를 미리 넣으려면 `connection.example.json`을 참고해 EXE 옆에 `connection.local.json`을 만듭니다. 로컬 설정 파일은 Git에서 제외하며 공개 배포에는 포함하지 않습니다. 이 파일이 없으면 첫 실행 시 주소를 입력받습니다.
- 앱은 계정·암호를 별도 설정 파일에 저장하지 않고 웹 엔진의 암호 자동 저장도 끕니다. DSM 화면에서 로그인 상태 유지를 선택할 수 있습니다. 로그인 지속 시간은 DSM의 세션 정책을 따릅니다.

트레이로 숨길 때 WebView를 폐기하거나 강제로 일시 정지하지 않습니다. 웹 엔진의 기본 백그라운드 타이머 조절은 적용될 수 있고, PC 절전·네트워크 단절·DSM 세션 만료가 화면 기능에 영향을 줄 수 있습니다. 이 앱은 파일 동기화 클라이언트가 아니므로 앱만 켜두었다고 PC 파일이 자동 백업되지는 않습니다. NAS 자체에서 실행하는 서버 작업은 NAS가 처리합니다.

## 연결과 호환성

인증서 검증을 해제하지 않습니다. 연결 오류 화면에서는 NAS 전원·네트워크·주소와 일반 브라우저의 HTTPS 인증서 상태를 확인하세요. 이 앱의 웹 콘텐츠에는 로컬 앱을 조작하는 호스트 객체나 메시지 브리지를 제공하지 않습니다.

내부 화면 이동은 설정한 HTTPS 호스트와 포트로 제한합니다. 다른 호스트·포트를 사용하는 Synology 패키지나 별도 인증 사이트는 추가 호환 처리가 필요할 수 있습니다. 사용자 클릭으로 요청된 외부 팝업은 주소를 표시한 후 기본 브라우저에서 열 수 있습니다. 커스텀 URL 프로토콜을 통한 외부 앱 실행은 차단합니다.

숨겨진 상태에도 웹 엔진은 메모리와 CPU 등 PC 자원을 사용합니다. 게임 성능을 위해 GPU 설정을 임의로 변경하거나 강제 절전을 적용하지 않습니다.

## 빌드와 확인

PowerShell에서 `./build.ps1`을 실행합니다. Windows 기본 .NET Framework C# 컴파일러와 NuGet의 Microsoft.Web.WebView2 **1.0.4314.35**를 사용하며, 처음에는 SDK 패키지를 내려받습니다. 실행에 필요한 SDK DLL은 결과 폴더에 복사됩니다. Evergreen Runtime은 배포 파일에 포함하지 않습니다.

`./test.ps1`은 보이지 않는 로컬 WebView 창과 가상 HTTPS 페이지로 검사합니다. 실제 NAS나 계정에 접근하지 않습니다. 결과는 `test-output/results.txt`에 남습니다.

검사한 항목은 실제 WebView2 초기화, X를 통한 숨김, 트레이 유지·창 복원, 숨김 중 JavaScript 실행, 쿠키·웹 저장소 유지, 팝업의 세션 공유·숨김·복원·웹에서 닫기, 출처 제한, 명시적 종료 시 창·트레이 정리, 주소 편집·취소·검증·저장 후 새 주소 접속, 최소 창 크기의 주소 입력줄 배치입니다. Windows 종료 처리는 종료 이유별 분기 검사이며 실제 로그오프나 재부팅은 하지 않았습니다. 실제 DSM 로그인·파일 전송·패키지별 호환성과 트레이 아이콘을 직접 누르는 화면 검증은 별도로 확인해야 합니다.

구현 근거: [Microsoft WebView2 WinForms](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/winforms), [NotifyIcon](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon), [WebView2 사용자 데이터 폴더](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder), [WebView2 보안](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security).
