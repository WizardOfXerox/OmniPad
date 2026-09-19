/**
 * OmniPad Gyroscope & Motion Engine
 * 1. 100Hz 6-Axis IMU sensor streaming over WebSocket/UDP (CemuHook DSU format).
 * 2. Physical shake detection (BetterJoy jerk spike math).
 * 3. Local capacitive gyro aiming & tilt steering fallback.
 */
class GyroEngine {
    constructor(touchEngine, networkClient = null) {
        this.touchEngine = touchEngine;
        this.network = networkClient;
        this.mode = 'off'; // 'off', 'aim', 'steer', 'mouse'
        this.touchOnly = true;
        this.sensX = 1.0;
        this.sensY = 1.0;

        this.lastGamma = null;
        this.lastBeta = null;
        this.smoothX = 0;
        this.smoothY = 0;
        this.filterAlpha = 0.35; // Low-pass filter smoothing

        this.isAimingActive = false;
        this.isEnabled = false;

        // Physical shake detection (BetterJoy math: jerk spike > 1.5G)
        this.lastShakeTime = 0;
        this.shakeCooldownMs = 400;
        this.onShake = null;
    }

    async init() {
        if (typeof DeviceMotionEvent !== 'undefined' && typeof DeviceMotionEvent.requestPermission === 'function') {
            try {
                const response = await DeviceMotionEvent.requestPermission();
                if (response === 'granted') {
                    this.startListening();
                    return true;
                }
            } catch {
                return false;
            }
        } else {
            this.startListening();
            return true;
        }
        return false;
    }

    startListening() {
        if (this.isEnabled) return;
        this.isEnabled = true;

        // 1. High-rate 6-axis hardware motion listener
        if (window.DeviceMotionEvent) {
            window.addEventListener('devicemotion', (e) => this.handleMotion(e), { passive: true });
        }

        // 2. Orientation listener for local stick aiming / steering
        if (window.DeviceOrientationEvent) {
            window.addEventListener('deviceorientation', (e) => this.handleOrientation(e), { passive: true });
        }
    }

    handleMotion(e) {
        const accel = e.accelerationIncludingGravity || e.acceleration;
        const rot = e.rotationRate;

        if (!accel) return;

        // Convert m/s^2 to G
        const G = 9.80665;
        const ax = (accel.x || 0) / G;
        const ay = (accel.y || 0) / G;
        const az = (accel.z || 0) / G;

        const gx = rot ? (rot.beta || 0) : 0;   // deg/s pitch
        const gy = rot ? (rot.gamma || 0) : 0;  // deg/s yaw
        const gz = rot ? (rot.alpha || 0) : 0;  // deg/s roll

        // 1. Shake Detection (BetterJoy dynamic jerk)
        const accelMag = Math.sqrt(ax * ax + ay * ay + az * az);
        const dynamicG = Math.abs(accelMag - 1.0);
        if (dynamicG >= 1.5) {
            const now = performance.now();
            if (now - this.lastShakeTime >= this.shakeCooldownMs) {
                this.lastShakeTime = now;
                this.touchEngine.triggerHaptic(35);
                if (this.onShake) {
                    this.onShake();
                } else {
                    // Default shake gesture: pulse Touchpad click or button
                    this.touchEngine.setButton(this.touchEngine.BUTTONS.TOUCHPAD, true);
                    setTimeout(() => {
                        this.touchEngine.setButton(this.touchEngine.BUTTONS.TOUCHPAD, false);
                    }, 60);
                }
            }
        }

        // 2. Stream 6-axis IMU packet to PC server (for CemuHook / ViGEm / JSM)
        if (this.network && this.network.isConnected) {
            this.network.sendMotion({
                accelX: ax,
                accelY: ay,
                accelZ: az,
                gyroX: gx,
                gyroY: gy,
                gyroZ: gz
            });
        }
    }

    handleOrientation(e) {
        if (this.mode === 'off') return;

        // Capacitive aim check: if touch-only is true, gyro only engages when aiming or ADS held
        if (this.touchOnly && !this.isAimingActive && this.touchEngine.state.leftTrigger < 100) {
            this.lastGamma = null;
            this.lastBeta = null;
            return;
        }

        const gamma = e.gamma || 0; // Roll: Left/Right tilt (-90 to 90)
        const beta = e.beta || 0;   // Pitch: Forward/Backward tilt (-180 to 180)

        if (this.lastGamma === null || this.lastBeta === null) {
            this.lastGamma = gamma;
            this.lastBeta = beta;
            return;
        }

        let deltaX = (gamma - this.lastGamma);
        let deltaY = (beta - this.lastBeta);
        this.lastGamma = gamma;
        this.lastBeta = beta;

        // Apply low pass filter
        this.smoothX = this.smoothX * (1 - this.filterAlpha) + deltaX * this.filterAlpha;
        this.smoothY = this.smoothY * (1 - this.filterAlpha) + deltaY * this.filterAlpha;

        if (this.mode === 'aim') {
            // Right Stick aiming
            let normX = Math.max(-1.0, Math.min(1.0, (this.smoothX * this.sensX) / 8));
            let normY = Math.max(-1.0, Math.min(1.0, (-this.smoothY * this.sensY) / 8));
            this.touchEngine.setStick('right', normX, normY);
        } else if (this.mode === 'steer') {
            // Left Stick steering (Roll tilt)
            let steerNorm = Math.max(-1.0, Math.min(1.0, (gamma / 35.0) * this.sensX));
            this.touchEngine.setStick('left', steerNorm, 0);
        }
    }

    setAimingTouchActive(active) {
        this.isAimingActive = active;
    }
}
