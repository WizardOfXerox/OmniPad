import SwiftUI

@main
struct OmniPadApp: App {
    @UIApplicationDelegateAdaptor(AppDelegate.self) var appDelegate

    init() {
        // Prevent screen dimming and auto-lock during gameplay sessions
        UIApplication.shared.isIdleTimerDisabled = true

        // Initialize tactical volume bumper hardware hooks and haptics
        _ = VolumeButtonBumper.shared
        _ = HapticEngine.shared
    }

    var body: some Scene {
        WindowGroup {
            ContentView()
                .preferredColorScheme(.dark)
                .ignoresSafeArea()
        }
    }
}

class AppDelegate: NSObject, UIApplicationDelegate {
    func application(
        _ application: UIApplication,
        supportedInterfaceOrientationsFor window: UIWindow?
    ) -> UIInterfaceOrientationMask {
        // Strict landscape lock for optimal two-thumb ergonomics
        return [.landscapeLeft, .landscapeRight]
    }
}
