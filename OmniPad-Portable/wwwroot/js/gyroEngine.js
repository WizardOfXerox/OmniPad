/**
 * OmniPad Gyroscope & Motion Engine
 * Motion-based aiming, tilt steering, and capacitive touch activation.
 */
class GyroEngine {
    constructor(touchEngine) {
        this.touchEngine = touchEngine;
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
    }

    async init() {
        if (typeof DeviceOrientationEvent !== 'undefined' && typeof DeviceOrientationEvent.requestPermission === 'function') {
            try {
                const response = await DeviceOrientationEvent.requestPermission();
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
        window.addEventListener('deviceorientation', (e) => this.handleOrientation(e));
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
