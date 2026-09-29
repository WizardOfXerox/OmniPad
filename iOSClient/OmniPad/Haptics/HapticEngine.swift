import Foundation
import CoreHaptics
import UIKit

/// High-fidelity haptic feedback engine utilizing Apple CoreHaptics.
/// Translates dual-motor PC rumble packets (large heavy motor, small crisp motor)
/// into precise physical vibrations, with fallback support for UIImpactFeedbackGenerator.
public final class HapticEngine: ObservableObject {
    public static let shared = HapticEngine()

    private var hapticEngine: CHHapticEngine?
    private var continuousPlayer: CHHapticAdvancedPatternPlayer?
    private let supportsCoreHaptics: Bool

    // Fallback generators for older iOS devices or when CoreHaptics is disabled
    private let lightImpact = UIImpactFeedbackGenerator(style: .light)
    private let mediumImpact = UIImpactFeedbackGenerator(style: .medium)
    private let heavyImpact = UIImpactFeedbackGenerator(style: .heavy)
    private let rigidImpact = UIImpactFeedbackGenerator(style: .rigid)

    private var isPlayingRumble = false
    private var lastLargeMotor: UInt8 = 0
    private var lastSmallMotor: UInt8 = 0
    private let queue = DispatchQueue(label: "com.omniPad.haptics", qos: .userInteractive)

    public init() {
        self.supportsCoreHaptics = CHHapticEngine.capabilitiesForHardware().supportsHaptics
        if supportsCoreHaptics {
            setupEngine()
        }
    }

    private func setupEngine() {
        do {
            let engine = try CHHapticEngine()
            engine.playsHapticsOnly = true
            engine.isAutoShutdownEnabled = false

            engine.resetHandler = { [weak self] in
                self?.queue.async {
                    do {
                        try self?.hapticEngine?.start()
                    } catch {
                        print("[HapticEngine] Failed to restart after reset: \(error)")
                    }
                }
            }

            engine.stoppedHandler = { reason in
                print("[HapticEngine] Engine stopped, reason: \(reason)")
            }

            try engine.start()
            self.hapticEngine = engine
        } catch {
            print("[HapticEngine] Failed to initialize CHHapticEngine: \(error)")
        }
    }

    /// Handles incoming rumble packets from PC Server (large: 0-255, small: 0-255).
    public func handleRumble(large: UInt8, small: UInt8) {
        queue.async { [weak self] in
            guard let self = self else { return }

            if large == 0 && small == 0 {
                self.stopRumbleInternal()
                self.lastLargeMotor = 0
                self.lastSmallMotor = 0
                return
            }

            if self.lastLargeMotor == large && self.lastSmallMotor == small && self.isPlayingRumble {
                return
            }

            self.lastLargeMotor = large
            self.lastSmallMotor = small

            let largeIntensity = Float(large) / 255.0
            let smallIntensity = Float(small) / 255.0

            if self.supportsCoreHaptics, let engine = self.hapticEngine {
                self.playDynamicRumble(engine: engine, large: largeIntensity, small: smallIntensity)
            } else {
                // Fallback for devices without CoreHaptics
                DispatchQueue.main.async {
                    if max(large, small) > 180 {
                        self.heavyImpact.impactOccurred()
                    } else if max(large, small) > 80 {
                        self.mediumImpact.impactOccurred()
                    } else {
                        self.lightImpact.impactOccurred()
                    }
                }
            }
        }
    }

    private func playDynamicRumble(engine: CHHapticEngine, large: Float, small: Float) {
        do {
            // Stop any existing continuous player
            stopRumbleInternal()

            var events: [CHHapticEvent] = []

            // Large motor: Heavy, low sharpness rumble (bass, explosions, traction loss)
            if large > 0.05 {
                let largeIntensityParam = CHHapticEventParameter(parameterID: .hapticIntensity, value: large)
                let largeSharpnessParam = CHHapticEventParameter(parameterID: .hapticSharpness, value: 0.2)
                let largeEvent = CHHapticEvent(
                    eventType: .hapticContinuous,
                    parameters: [largeIntensityParam, largeSharpnessParam],
                    relativeTime: 0,
                    duration: 1.0 // Loops or gets refreshed continuously
                )
                events.append(largeEvent)
            }

            // Small motor: High sharpness, crisp motor vibration (gun clatter, road bumps, buzz)
            if small > 0.05 {
                let smallIntensityParam = CHHapticEventParameter(parameterID: .hapticIntensity, value: small)
                let smallSharpnessParam = CHHapticEventParameter(parameterID: .hapticSharpness, value: 0.85)
                let smallEvent = CHHapticEvent(
                    eventType: .hapticContinuous,
                    parameters: [smallIntensityParam, smallSharpnessParam],
                    relativeTime: 0,
                    duration: 1.0
                )
                events.append(smallEvent)
            }

            guard !events.isEmpty else { return }

            let pattern = try CHHapticPattern(events: events, parameters: [])
            let player = try engine.makeAdvancedPlayer(with: pattern)
            player.loopEnabled = true

            try player.start(atTime: CHHapticTimeImmediate)
            self.continuousPlayer = player
            self.isPlayingRumble = true
        } catch {
            print("[HapticEngine] Error playing continuous rumble: \(error)")
        }
    }

    private func stopRumbleInternal() {
        if isPlayingRumble, let player = continuousPlayer {
            do {
                try player.stop(atTime: CHHapticTimeImmediate)
            } catch {
                // Ignore stop error
            }
            continuousPlayer = nil
            isPlayingRumble = false
        }
    }

    /// Triggers immediate tactical haptic click (e.g. for hardware volume bumpers LB/RB).
    public func triggerTactileClick() {
        queue.async { [weak self] in
            guard let self = self else { return }
            if self.supportsCoreHaptics, let engine = self.hapticEngine {
                do {
                    let intensity = CHHapticEventParameter(parameterID: .hapticIntensity, value: 0.95)
                    let sharpness = CHHapticEventParameter(parameterID: .hapticSharpness, value: 0.9)
                    let event = CHHapticEvent(eventType: .hapticTransient, parameters: [intensity, sharpness], relativeTime: 0)
                    let pattern = try CHHapticPattern(events: [event], parameters: [])
                    let player = try engine.makePlayer(with: pattern)
                    try player.start(atTime: CHHapticTimeImmediate)
                } catch {
                    DispatchQueue.main.async { self.rigidImpact.impactOccurred() }
                }
            } else {
                DispatchQueue.main.async { self.rigidImpact.impactOccurred() }
            }
        }
    }

    /// JS Bridge vibrate(durationMs): Transient UI touch feedback.
    public func vibrate(ms: Int) {
        queue.async { [weak self] in
            guard let self = self else { return }
            let duration = max(0.02, min(0.15, Double(ms) / 1000.0))
            if self.supportsCoreHaptics, let engine = self.hapticEngine {
                do {
                    let intensity = CHHapticEventParameter(parameterID: .hapticIntensity, value: 0.6)
                    let sharpness = CHHapticEventParameter(parameterID: .hapticSharpness, value: 0.5)
                    let event = CHHapticEvent(eventType: .hapticTransient, parameters: [intensity, sharpness], relativeTime: 0)
                    let pattern = try CHHapticPattern(events: [event], parameters: [])
                    let player = try engine.makePlayer(with: pattern)
                    try player.start(atTime: CHHapticTimeImmediate)
                } catch {
                    DispatchQueue.main.async { self.mediumImpact.impactOccurred() }
                }
            } else {
                DispatchQueue.main.async { self.mediumImpact.impactOccurred() }
            }
        }
    }

    /// JS Bridge vibrateHeavy(durationMs): Heavy transient feedback (gun recoil, crash).
    public func vibrateHeavy(ms: Int) {
        queue.async { [weak self] in
            guard let self = self else { return }
            if self.supportsCoreHaptics, let engine = self.hapticEngine {
                do {
                    let intensity = CHHapticEventParameter(parameterID: .hapticIntensity, value: 1.0)
                    let sharpness = CHHapticEventParameter(parameterID: .hapticSharpness, value: 0.7)
                    let event = CHHapticEvent(eventType: .hapticTransient, parameters: [intensity, sharpness], relativeTime: 0)
                    let pattern = try CHHapticPattern(events: [event], parameters: [])
                    let player = try engine.makePlayer(with: pattern)
                    try player.start(atTime: CHHapticTimeImmediate)
                } catch {
                    DispatchQueue.main.async { self.heavyImpact.impactOccurred() }
                }
            } else {
                DispatchQueue.main.async { self.heavyImpact.impactOccurred() }
            }
        }
    }
}
