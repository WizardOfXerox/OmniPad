import SwiftUI
import WebKit

/// A high-performance WKWebView container optimized for low-latency gamepad streaming.
/// Features offline WebClient bundling, edge-to-edge landscape touch handling,
/// and bidirectional JavaScript bridge matching window.OmniPadNative.
public struct WebViewContainer: UIViewRepresentable {
    @ObservedObject var appState: AppState
    public var onScanQrRequested: () -> Void
    public var onServerSelectRequested: () -> Void

    public init(
        appState: AppState,
        onScanQrRequested: @escaping () -> Void,
        onServerSelectRequested: @escaping () -> Void
    ) {
        self.appState = appState
        self.onScanQrRequested = onScanQrRequested
        self.onServerSelectRequested = onServerSelectRequested
    }

    public func makeUIView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.allowsInlineMediaPlayback = true
        config.mediaTypesRequiringUserActionForPlayback = []
        config.defaultWebpagePreferences.allowsContentJavaScript = true

        // 1. Inject JavaScript Bridge Script at document start
        let bridgeJs = """
        (function() {
            window.__isOmniPadIOS = true;

            // OmniPad Native Bridge for iOS
            window.OmniPadNative = {
                vibrate: function(ms) {
                    try {
                        window.webkit.messageHandlers.OmniPadNative.postMessage({ action: 'vibrate', duration: ms || 20 });
                    } catch(e) {}
                },
                vibrateHeavy: function(ms) {
                    try {
                        window.webkit.messageHandlers.OmniPadNative.postMessage({ action: 'vibrateHeavy', duration: ms || 50 });
                    } catch(e) {}
                },
                isUsbConnected: function() {
                    return false;
                },
                scanAndSelectServer: function() {
                    try {
                        window.webkit.messageHandlers.OmniPadNative.postMessage({ action: 'scanAndSelectServer' });
                    } catch(e) {}
                },
                scanQrCode: function() {
                    try {
                        window.webkit.messageHandlers.OmniPadNative.postMessage({ action: 'scanQrCode' });
                    } catch(e) {}
                },
                getBluetoothStatus: function() {
                    return 'unsupported';
                },
                getBluetoothHost: function() {
                    return '';
                },
                startBluetoothHid: function() {
                    return false;
                },
                stopBluetoothHid: function() {
                    try {
                        window.webkit.messageHandlers.OmniPadNative.postMessage({ action: 'stopBluetoothHid' });
                    } catch(e) {}
                },
                sendBluetoothReport: function(buttons, lx, ly, rx, ry, lt, rt) {
                    try {
                        window.webkit.messageHandlers.OmniPadNative.postMessage({
                            action: 'sendBluetoothReport',
                            buttons: buttons || 0,
                            lx: lx || 0,
                            ly: ly || 0,
                            rx: rx || 0,
                            ry: ry || 0,
                            lt: lt || 0,
                            rt: rt || 0
                        });
                        return true;
                    } catch(e) { return false; }
                },
                sendUdpInput: function(buttons, lx, ly, rx, ry, lt, rt) {
                    try {
                        window.webkit.messageHandlers.OmniPadNative.postMessage({
                            action: 'sendUdpInput',
                            buttons: buttons || 0,
                            lx: lx || 0,
                            ly: ly || 0,
                            rx: rx || 0,
                            ry: ry || 0,
                            lt: lt || 0,
                            rt: rt || 0
                        });
                    } catch(e) {}
                },
                switchToBluetoothMode: function() {
                    try {
                        window.webkit.messageHandlers.OmniPadNative.postMessage({ action: 'switchToBluetoothMode' });
                    } catch(e) {}
                },
                startNativeMic: function() { return false; },
                stopNativeMic: function() { return true; },
                isNativeMicStreaming: function() { return false; },
                getNativeMicLevel: function() { return 0.0; },
                setNativeMicMuted: function(muted) {}
            };
        })();
        """

        let script = WKUserScript(source: bridgeJs, injectionTime: .atDocumentStart, forMainFrameOnly: false)
        config.userContentController.addUserScript(script)
        config.userContentController.add(context.coordinator, name: "OmniPadNative")

        // 2. Instantiate and configure Gamepad WKWebView
        let webView = GamepadWKWebView(frame: .zero, configuration: config)
        webView.navigationDelegate = context.coordinator
        webView.uiDelegate = context.coordinator

        webView.backgroundColor = UIColor(red: 0.04, green: 0.05, blue: 0.08, alpha: 1.0) // #0a0d14
        webView.isOpaque = false

        // Disable scrolling and bouncing for crisp physical feel
        webView.scrollView.isScrollEnabled = false
        webView.scrollView.bounces = false
        webView.scrollView.contentInsetAdjustmentBehavior = .never
        webView.isMultipleTouchEnabled = true
        webView.scrollView.isMultipleTouchEnabled = true

        context.coordinator.webView = webView
        appState.activeWebView = webView

        // Wire physical hardware volume bumpers directly to JS
        VolumeButtonBumper.shared.onBumperAction = { [weak webView] bumper, isDown in
            let btnName = (bumper == .lb) ? "LB" : "RB"
            let js = "if (window.omniPadTriggerButton) window.omniPadTriggerButton('\(btnName)', \(isDown ? "true" : "false"));"
            DispatchQueue.main.async {
                webView?.evaluateJavaScript(js, completionHandler: nil)
            }
        }

        // 3. Load initial content
        loadTarget(webView: webView)

        return webView
    }

    public func updateUIView(_ uiView: WKWebView, context: Context) {
        if appState.reloadRequested {
            DispatchQueue.main.async {
                self.appState.reloadRequested = false
                self.loadTarget(webView: uiView)
            }
        }
    }

    private func loadTarget(webView: WKWebView) {
        if appState.isOfflineMode {
            loadOfflineBundle(webView: webView)
        } else if let targetUrl = appState.serverUrl {
            let request = URLRequest(url: targetUrl, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 5.0)
            webView.load(request)
        } else {
            loadOfflineBundle(webView: webView)
        }
    }

    private func loadOfflineBundle(webView: WKWebView) {
        // Look for WebClient/index.html in bundle
        if let offlineUrl = Bundle.main.url(forResource: "index", withExtension: "html", subdirectory: "Web") {
            webView.loadFileURL(offlineUrl, allowingReadAccessTo: offlineUrl.deletingLastPathComponent())
            return
        }

        if let rootHtml = Bundle.main.url(forResource: "index", withExtension: "html") {
            webView.loadFileURL(rootHtml, allowingReadAccessTo: rootHtml.deletingLastPathComponent())
            return
        }

        // Fallback placeholder if resources are missing
        let fallbackHtml = """
        <!DOCTYPE html><html><body style="background:#0a0d14;color:#38bdf8;font-family:system-ui;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;">
        <div style="text-align:center;"><h2>OmniPad iOS Ready</h2><p style="color:#94a3b8;">Connect to PC Server or Scan QR Code</p></div>
        </body></html>
        """
        webView.loadHTMLString(fallbackHtml, baseURL: nil)
    }

    public func makeCoordinator() -> Coordinator {
        Coordinator(
            appState: appState,
            onScanQrRequested: onScanQrRequested,
            onServerSelectRequested: onServerSelectRequested
        )
    }

    public final class Coordinator: NSObject, WKNavigationDelegate, WKUIDelegate, WKScriptMessageHandler {
        weak var webView: WKWebView?
        let appState: AppState
        let onScanQrRequested: () -> Void
        let onServerSelectRequested: () -> Void

        init(appState: AppState, onScanQrRequested: @escaping () -> Void, onServerSelectRequested: @escaping () -> Void) {
            self.appState = appState
            self.onScanQrRequested = onScanQrRequested
            self.onServerSelectRequested = onServerSelectRequested
        }

        // Handle JS bridge messages
        public func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
            guard let dict = message.body as? [String: Any],
                  let action = dict["action"] as? String else {
                return
            }

            switch action {
            case "vibrate":
                let duration = dict["duration"] as? Int ?? 20
                HapticEngine.shared.vibrate(ms: duration)

            case "vibrateHeavy":
                let duration = dict["duration"] as? Int ?? 50
                HapticEngine.shared.vibrateHeavy(ms: duration)

            case "scanQrCode":
                DispatchQueue.main.async { self.onScanQrRequested() }

            case "scanAndSelectServer":
                DispatchQueue.main.async { self.onServerSelectRequested() }

            case "sendBluetoothReport", "sendUdpInput":
                let btns = dict["buttons"] as? UInt16 ?? UInt16(dict["buttons"] as? Int ?? 0)
                let lx = dict["lx"] as? Int16 ?? Int16(dict["lx"] as? Int ?? 0)
                let ly = dict["ly"] as? Int16 ?? Int16(dict["ly"] as? Int ?? 0)
                let rx = dict["rx"] as? Int16 ?? Int16(dict["rx"] as? Int ?? 0)
                let ry = dict["ry"] as? Int16 ?? Int16(dict["ry"] as? Int ?? 0)
                let lt = dict["lt"] as? UInt8 ?? UInt8(dict["lt"] as? Int ?? 0)
                let rt = dict["rt"] as? UInt8 ?? UInt8(dict["rt"] as? Int ?? 0)

                UdpTransport.shared.sendImmediate(buttons: btns, lx: lx, ly: ly, rx: rx, ry: ry, lt: lt, rt: rt)

            case "switchToBluetoothMode":
                DispatchQueue.main.async {
                    self.appState.isOfflineMode = true
                    self.appState.reloadRequested = true
                }

            default:
                break
            }
        }

        public func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
            DispatchQueue.main.async {
                self.appState.isWebLoaded = true
            }
        }

        public func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
            handleFailure(webView: webView, error: error)
        }

        public func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
            handleFailure(webView: webView, error: error)
        }

        private func handleFailure(webView: WKWebView, error: Error) {
            print("[WebViewContainer] Navigation failed: \(error.localizedDescription)")
            DispatchQueue.main.async {
                self.appState.isWebLoaded = false
                // Auto-fallback to offline bundle on initial connection failure
                if !self.appState.isOfflineMode {
                    DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) {
                        self.appState.isOfflineMode = true
                        self.appState.reloadRequested = true
                    }
                }
            }
        }
    }
}

/// Custom WKWebView subclass with touch callouts disabled for edge-to-edge responsiveness
public final class GamepadWKWebView: WKWebView {
    public override var canBecomeFirstResponder: Bool { true }

    public override func canPerformAction(_ action: Selector, withSender sender: Any?) -> Bool {
        // Suppress copy/paste/select popup menus during gameplay
        return false
    }
}
