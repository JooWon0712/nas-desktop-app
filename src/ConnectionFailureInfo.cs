using System.Globalization;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal sealed class ConnectionFailureInfo
    {
        internal string Title { get; private set; }
        internal string Description { get; private set; }
        internal string Detail { get; private set; }

        private ConnectionFailureInfo(string title, string description, string detail)
        {
            Title = title;
            Description = description;
            Detail = detail;
        }

        internal static ConnectionFailureInfo FromWebError(CoreWebView2WebErrorStatus status, int httpStatusCode = 0)
        {
            // Cancellation and authentication challenges are not connection failures.
            // Keep the NAS response or native authentication flow available.
            if (status == CoreWebView2WebErrorStatus.OperationCanceled
                || status == CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired
                || status == CoreWebView2WebErrorStatus.ValidProxyAuthenticationRequired
                || httpStatusCode == 401 || httpStatusCode == 403 || httpStatusCode == 407)
                return null;

            ConnectionFailureInfo http = FromHttpStatus(httpStatusCode);
            if (http != null) return http;

            string title;
            string description;
            switch (status)
            {
                case CoreWebView2WebErrorStatus.CannotConnect:
                    title = "NAS에 연결할 수 없습니다";
                    description = "NAS의 전원과 HTTPS 관리 포트, 접속 서비스가 켜져 있는지 확인해 주세요.\n연결이 거부되었거나 해당 포트에서 서비스가 실행되지 않을 수 있습니다.";
                    break;
                case CoreWebView2WebErrorStatus.HostNameNotResolved:
                    title = "NAS 주소를 찾을 수 없습니다";
                    description = "호스트 이름의 철자와 DNS 설정을 확인해 주세요.\n내부 주소라면 NAS와 같은 네트워크 또는 연결에 필요한 VPN을 사용하고 있는지 확인해 주세요.";
                    break;
                case CoreWebView2WebErrorStatus.Timeout:
                    title = "NAS의 응답 시간이 초과되었습니다";
                    description = "NAS가 꺼져 있거나 네트워크 응답이 지연될 수 있습니다.\nNAS의 전원과 네트워크 연결을 확인한 뒤 다시 연결해 주세요.";
                    break;
                case CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect:
                case CoreWebView2WebErrorStatus.CertificateExpired:
                case CoreWebView2WebErrorStatus.ClientCertificateContainsErrors:
                case CoreWebView2WebErrorStatus.CertificateRevoked:
                case CoreWebView2WebErrorStatus.CertificateIsInvalid:
                    title = "HTTPS 인증서를 확인해 주세요";
                    description = status == CoreWebView2WebErrorStatus.ClientCertificateContainsErrors
                        ? "접속에 필요한 클라이언트 인증서를 확인할 수 없습니다.\nNAS 관리자에게 인증서와 접속 설정을 확인해 주세요."
                        : "주소와 인증서 이름이 맞지 않거나 인증서를 신뢰할 수 없는 상태일 수 있습니다.\nNAS의 인증서를 확인하고, 정상 인증서가 적용된 HTTPS 관리 주소로 다시 연결해 주세요.";
                    break;
                case CoreWebView2WebErrorStatus.Disconnected:
                    title = "네트워크 연결을 확인해 주세요";
                    description = "네트워크 연결이 끊어졌습니다.\nPC의 유선·Wi-Fi 연결과 NAS에 접근하는 네트워크를 확인한 뒤 다시 연결해 주세요.";
                    break;
                case CoreWebView2WebErrorStatus.ServerUnreachable:
                    title = "NAS에 도달할 수 없습니다";
                    description = "현재 네트워크에서 NAS에 도달하지 못했습니다.\nNAS의 전원, PC의 네트워크와 필요한 VPN 연결을 확인해 주세요.";
                    break;
                case CoreWebView2WebErrorStatus.ConnectionAborted:
                case CoreWebView2WebErrorStatus.ConnectionReset:
                    title = "NAS 연결이 중단되었습니다";
                    description = "연결이 완료되기 전에 통신이 중단되었습니다.\n네트워크 상태와 NAS의 접속 서비스를 확인한 뒤 다시 연결해 주세요.";
                    break;
                case CoreWebView2WebErrorStatus.RedirectFailed:
                    title = "NAS의 연결 경로를 열 수 없습니다";
                    description = "주소 전환을 완료하지 못했습니다.\n접속 방식을 확인하고 NAS의 최종 HTTPS 관리 주소로 다시 연결해 주세요.";
                    break;
                case CoreWebView2WebErrorStatus.ErrorHttpInvalidServerResponse:
                    title = "NAS의 응답을 읽을 수 없습니다";
                    description = "NAS가 정상적인 웹 응답을 보내지 않았습니다.\nHTTPS 관리 주소와 포트, NAS의 웹 접속 서비스 설정을 확인해 주세요.";
                    break;
                default:
                    title = "NAS에 연결할 수 없습니다";
                    description = "연결을 완료하지 못했습니다.\nNAS의 주소·포트와 전원, 네트워크 상태를 확인한 뒤 다시 연결해 주세요.";
                    break;
            }
            return new ConnectionFailureInfo(title, description, status.ToString());
        }

        internal static ConnectionFailureInfo FromHttpStatus(int code)
        {
            if (code < 400 || code > 599 || code == 401 || code == 403 || code == 407) return null;
            string detail = "HTTP " + code.ToString(CultureInfo.InvariantCulture);
            if (code == 404 || code == 410)
                return new ConnectionFailureInfo("NAS 관리 페이지를 찾을 수 없습니다",
                    "이 주소에 관리 페이지가 없거나 경로가 변경되었을 수 있습니다.\n주소의 경로와 관리 포트를 확인하고 다시 연결해 주세요.", detail);
            if (code == 429)
                return new ConnectionFailureInfo("잠시 후 다시 연결해 주세요",
                    "NAS가 요청이 너무 많다고 응답했습니다.\n잠시 기다린 뒤 다시 연결해 주세요.", detail);
            if (code == 408)
                return new ConnectionFailureInfo("NAS의 응답 시간이 초과되었습니다",
                    "NAS가 요청 시간이 초과되었다고 응답했습니다.\n네트워크 상태를 확인한 뒤 다시 연결해 주세요.", detail);
            if (code == 502 || code == 503 || code == 504)
                return new ConnectionFailureInfo("NAS 서비스를 사용할 수 없습니다",
                    "NAS 또는 접속을 중계하는 서비스가 정상 응답을 보내지 못했습니다.\n잠시 후 다시 연결하거나 NAS 관리자에게 서비스 상태를 확인해 주세요.", detail);
            if (code >= 500)
                return new ConnectionFailureInfo("NAS에서 오류 응답을 보냈습니다",
                    "NAS가 요청을 처리하는 중 오류를 보냈습니다.\n잠시 후 다시 연결하고, 문제가 계속되면 NAS의 웹 서비스 상태를 확인해 주세요.", detail);
            return new ConnectionFailureInfo("NAS 요청을 처리할 수 없습니다",
                "NAS가 이 요청을 처리할 수 없다고 응답했습니다.\n관리 주소의 경로와 접속 방식을 확인해 주세요.", detail);
        }
    }
}
