import Foundation
import AVFoundation
import MediaPlayer
import UIKit

public enum BumperButton {
    case lb
    case rb
}

/// Intercepts physical iOS hardware volume buttons (Volume Up -> LB, Volume Down -> RB)
/// using AVAudioSession outputVolume observation and a hidden MPVolumeView to suppress
/// the system volume overlay HUD, providing physical tactical bumper parity with Android.
public final class VolumeButtonBumper: ObservableObject {
    public static let shared = VolumeButtonBumper()

    @Published public var isEnabled: Bool = true
    @Published public var lastTriggeredBumper: BumperButton? = nil

    public var onBumperAction: ((BumperButton, Bool) -> Void)?

    private var volumeView: MPVolumeView?
    private var volumeSlider: UISlider?
    private var outputVolumeObservation: NSKeyValueObservation?
    private var lastRecordedVolume: Float = 0.5
    private var isResettingVolume: Bool = false
    private let releaseDelay: TimeInterval = 0.12 // 120ms tactical release window
    private var lbReleaseTimer: Timer?
    private var rbReleaseTimer: Timer?

    private init() {
        setupVolumeInterception()
    }

    /// Sets up the hidden MPVolumeView and AVAudioSession observation.
    public func setupVolumeInterception() {
        do {
            let session = AVAudioSession.sharedInstance()
            try session.setCategory(.ambient, options: [.mixWithOthers])
            try session.setActive(true)
            self.lastRecordedVolume = session.outputVolume
        } catch {
            print("[VolumeBumper] Failed to activate audio session: \(error)")
        }

        DispatchQueue.main.async { [weak self] in
            guard let self = self else { return }

            // 1. Create off-screen MPVolumeView to suppress system volume HUD
            let vView = MPVolumeView(frame: CGRect(x: -2000, y: -2000, width: 1, height: 1))
            vView.alpha = 0.0001
            vView.clipsToBounds = true

            // Attach to key window
            if let window = UIApplication.shared.connectedScenes
                .compactMap({ $0 as? UIWindowScene })
                .flatMap({ $0.windows })
                .first(where: { $0.isKeyWindow }) {
                window.addSubview(vView)
            }

            self.volumeView = vView

            // Find internal volume slider for silent midpoint resets
            for subview in vView.subviews {
                if let slider = subview as? UISlider {
                    self.volumeSlider = slider
                    break
                }
            }

            // Set initial midpoint volume to prevent hitting 0.0 or 1.0 limits
            self.resetVolumeToMidpoint()

            // 2. Observe outputVolume changes via KVO
            self.outputVolumeObservation = AVAudioSession.sharedInstance().observe(\.outputVolume, options: [.old, .new]) { [weak self] session, change in
                guard let self = self, self.isEnabled else { return }
                guard !self.isResettingVolume else { return }

                let newVol = change.newValue ?? session.outputVolume
                let oldVol = change.oldValue ?? self.lastRecordedVolume

                if abs(newVol - oldVol) < 0.001 { return }

                DispatchQueue.main.async {
                    if newVol > oldVol || newVol >= 0.99 {
                        // Volume Up -> LB (Left Bumper)
                        self.triggerBumper(.lb)
                    } else if newVol < oldVol || newVol <= 0.01 {
                        // Volume Down -> RB (Right Bumper)
                        self.triggerBumper(.rb)
                    }

                    // Silently reset slider back to 0.5 midpoint
                    self.resetVolumeToMidpoint()
                }
            }
        }
    }

    private func resetVolumeToMidpoint() {
        guard let slider = volumeSlider else { return }
        isResettingVolume = true
        slider.setValue(0.5, animated: false)
        lastRecordedVolume = 0.5

        DispatchQueue.main.asyncAfter(deadline: .now() + 0.05) { [weak self] in
            self?.isResettingVolume = false
        }
    }

    private func triggerBumper(_ bumper: BumperButton) {
        lastTriggeredBumper = bumper
        HapticEngine.shared.triggerTactileClick()

        // 1. Signal Press (True)
        onBumperAction?(bumper, true)

        // 2. Schedule automatic release
        switch bumper {
        case .lb:
            lbReleaseTimer?.invalidate()
            lbReleaseTimer = Timer.scheduledTimer(withTimeInterval: releaseDelay, repeats: false) { [weak self] _ in
                self?.onBumperAction?(.lb, false)
            }
        case .rb:
            rbReleaseTimer?.invalidate()
            rbReleaseTimer = Timer.scheduledTimer(withTimeInterval: releaseDelay, repeats: false) { [weak self] _ in
                self?.onBumperAction?(.rb, false)
            }
        }
    }

    deinit {
        outputVolumeObservation?.invalidate()
        volumeView?.removeFromSuperview()
        lbReleaseTimer?.invalidate()
        rbReleaseTimer?.invalidate()
    }
}
