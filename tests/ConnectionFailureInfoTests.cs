using System;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class ConnectionFailureInfoTests
    {
        internal static void Run(Action<bool, string> check)
        {
            ConnectionFailureInfo connection = ConnectionFailureInfo.FromWebError(CoreWebView2WebErrorStatus.CannotConnect);
            check(connection.Description.Contains("포트") && connection.Description.Contains("거부되었거나")
                && connection.Detail == "CannotConnect",
                "Connection failure explains address and port checks without asserting a single cause");
            ConnectionFailureInfo dns = ConnectionFailureInfo.FromWebError(CoreWebView2WebErrorStatus.HostNameNotResolved);
            check(dns.Title.Contains("주소") && dns.Description.Contains("DNS") && dns.Detail == "HostNameNotResolved",
                "Name resolution failure gives DNS and hostname guidance");
            ConnectionFailureInfo timeout = ConnectionFailureInfo.FromWebError(CoreWebView2WebErrorStatus.Timeout);
            check(timeout.Title.Contains("시간") && timeout.Description.Contains("전원"),
                "Timeout guidance includes NAS availability and network checks");
            bool certificateMessages = true;
            foreach (CoreWebView2WebErrorStatus status in new[] {
                CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect,
                CoreWebView2WebErrorStatus.CertificateExpired,
                CoreWebView2WebErrorStatus.ClientCertificateContainsErrors,
                CoreWebView2WebErrorStatus.CertificateRevoked,
                CoreWebView2WebErrorStatus.CertificateIsInvalid })
            {
                ConnectionFailureInfo error = ConnectionFailureInfo.FromWebError(status);
                certificateMessages &= error.Title.Contains("인증서") && error.Description.Contains("인증서")
                    && !error.Description.Contains("무시") && !error.Description.Contains("우회")
                    && error.Detail == status.ToString();
            }
            check(certificateMessages, "Certificate failures keep validation in place and use certificate guidance");
            check(ConnectionFailureInfo.FromWebError(CoreWebView2WebErrorStatus.Disconnected).Title.Contains("네트워크")
                && ConnectionFailureInfo.FromWebError(CoreWebView2WebErrorStatus.ServerUnreachable).Description.Contains("네트워크"),
                "Disconnected and unreachable failures guide network checks");
            check(ConnectionFailureInfo.FromWebError(CoreWebView2WebErrorStatus.OperationCanceled) == null
                && ConnectionFailureInfo.FromWebError(CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired) == null
                && ConnectionFailureInfo.FromWebError(CoreWebView2WebErrorStatus.ValidProxyAuthenticationRequired) == null,
                "Canceled navigation and authentication challenges do not produce connection failure screens");
            check(ConnectionFailureInfo.FromHttpStatus(401) == null && ConnectionFailureInfo.FromHttpStatus(403) == null
                && ConnectionFailureInfo.FromHttpStatus(407) == null
                && ConnectionFailureInfo.FromWebError(CoreWebView2WebErrorStatus.Unknown, 403) == null,
                "HTTP authentication and permission responses remain available to the NAS login flow");
            check(ConnectionFailureInfo.FromHttpStatus(200) == null && ConnectionFailureInfo.FromHttpStatus(0) == null
                && ConnectionFailureInfo.FromHttpStatus(302) == null && ConnectionFailureInfo.FromHttpStatus(700) == null,
                "Non-error and invalid HTTP values do not replace a document");
            ConnectionFailureInfo missing = ConnectionFailureInfo.FromHttpStatus(404);
            check(missing.Description.Contains("경로") && missing.Detail == "HTTP 404",
                "Missing HTTP page guidance identifies the management path and exact status");
            check(ConnectionFailureInfo.FromHttpStatus(500).Description.Contains("웹 서비스")
                && ConnectionFailureInfo.FromHttpStatus(503).Description.Contains("서비스")
                && ConnectionFailureInfo.FromWebError(CoreWebView2WebErrorStatus.Unknown, 503).Detail == "HTTP 503",
                "HTTP server responses retain their status and suggest service recovery");
            ConnectionFailureInfo unknown = ConnectionFailureInfo.FromWebError((CoreWebView2WebErrorStatus)12345);
            check(!String.IsNullOrEmpty(unknown.Title) && !String.IsNullOrEmpty(unknown.Description)
                && unknown.Detail == "12345", "Unknown future web errors receive a useful generic failure screen");
        }
    }
}
