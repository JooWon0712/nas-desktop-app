namespace NasDesk
{
    internal static class BrowserScrollbarStyle
    {
        internal const string StyleId = "nas-desktop-visible-scrollbars";

        // Style only browser-native scrollbars. Keep the NAS page's overflow,
        // sizing, positioning and custom scroll controls unchanged.
        internal const string Script = @"(function () {
    'use strict';
    var id = 'nas-desktop-visible-scrollbars';
    var css = ':root, :root * { scrollbar-width: auto !important; scrollbar-color: auto !important; }' +
        ':root::-webkit-scrollbar, :root *::-webkit-scrollbar { width: 14px !important; height: 14px !important; display: block !important; opacity: 1 !important; }' +
        ':root::-webkit-scrollbar-track, :root *::-webkit-scrollbar-track, :root::-webkit-scrollbar-track-piece, :root *::-webkit-scrollbar-track-piece { background: rgba(127,127,127,0.14) !important; display: block !important; opacity: 1 !important; }' +
        ':root::-webkit-scrollbar-thumb, :root *::-webkit-scrollbar-thumb { background-color: #788490 !important; border: 2px solid rgba(28,33,38,0.65) !important; background-clip: padding-box !important; box-shadow: inset 0 0 0 1px rgba(255,255,255,0.55) !important; border-radius: 8px !important; min-height: 28px !important; display: block !important; opacity: 1 !important; }' +
        ':root::-webkit-scrollbar-thumb:hover, :root *::-webkit-scrollbar-thumb:hover, :root::-webkit-scrollbar-thumb:active, :root *::-webkit-scrollbar-thumb:active { background-color: #66727e !important; }' +
        ':root::-webkit-scrollbar-corner, :root *::-webkit-scrollbar-corner { background: rgba(127,127,127,0.14) !important; }';

    function install() {
        var parent = document.head || document.documentElement;
        if (!parent) return false;
        var style = document.getElementById(id);
        if (!style) {
            style = document.createElement('style');
            style.id = id;
            style.textContent = css;
        }
        if (style.parentNode !== parent || parent.lastChild !== style) parent.appendChild(style);
        return true;
    }

    if (!install()) {
        var observer = new MutationObserver(function () {
            if (install()) observer.disconnect();
        });
        observer.observe(document, { childList: true, subtree: true });
    }
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', install, { once: true });
    }
}());";
    }
}
