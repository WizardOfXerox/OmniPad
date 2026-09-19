using System;
using OmniPadServer.Core;

namespace OmniPadServer.App;

/// <summary>
/// Next-generation Gyro Aiming and Motion Engine incorporating:
/// 1. JoyShockMapper natural sensitivity, velocity-based micro-tremor dampening, and auto-rest bias calibration.
/// 2. Jibb Smart Flick Stick (instant angular snap + smooth perimeter rolling).
/// 3. BetterJoy physical shake detection.
/// </summary>
public sealed class GyroAimEngine
{
    // --- Natural Sensitivity & Micro-Tremor Filtering ---
    public bool Enabled { get; set; } = true;
    public float SensitivityX { get; set; } = 1.0f;
    public float SensitivityY { get; set; } = 1.0f;
    public bool InvertX { get; set; } = false;
    public bool InvertY { get; set; } = false;

    /// <summary>
    /// Velocities below this threshold (in deg/s) are suppressed to eliminate hand tremor.
    /// </summary>
    public float MinTremorThreshold { get; set; } = 3.5f;

    /// <summary>
    /// Velocities above this threshold (in deg/s) are mapped 1:1 without dampening.
    /// </summary>
    public float MaxTremorThreshold { get; set; } = 25.0f;

    // --- Continuous Auto-Rest Calibration ---
    public bool AutoRestCalibrationEnabled { get; set; } = true;
    public float StillVelocityThreshold { get; set; } = 1.5f; // deg/s
    public float StillGravityDeltaThreshold { get; set; } = 0.08f; // G deviation from 1.0G
    public double RestDurationRequiredSeconds { get; set; } = 0.8; // seconds

    public float BiasX { get; private set; }
    public float BiasY { get; private set; }
    public float BiasZ { get; private set; }

    private double _stillTimer;
    private double _accumGyroX;
    private double _accumGyroY;
    private double _accumGyroZ;
    private int _accumCount;

    // --- Physical Shake Detector (BetterJoy math) ---
    public bool ShakeDetectionEnabled { get; set; } = true;
    public float ShakeThresholdG { get; set; } = 1.4f; // Excess G beyond 1.0G
    public TimeSpan ShakeCooldown { get; set; } = TimeSpan.FromMilliseconds(400);

    private DateTime _lastShakeTime = DateTime.MinValue;
    public event Action? OnShake;

    // --- Jibb Smart Flick Stick ---
    public bool FlickStickEnabled { get; set; } = false;
    public float FlickStickDeadzone { get; set; } = 0.6f; // Normalized radius (0.0 to 1.0)
    public float FlickStickHysteresis { get; set; } = 0.45f;
    public float SnapDurationSeconds { get; set; } = 0.09f; // Snap completed over ~90ms

    private bool _isFlickEngaged;
    private float _lastStickAngleDeg;
    private float _snapAngleRemainingDeg;
    private float _snapAngleTotalDeg;
    private double _snapElapsedSeconds;

    /// <summary>
    /// Resets runtime gyro calibration biases.
    /// </summary>
    public void ResetCalibration()
    {
        BiasX = 0f;
        BiasY = 0f;
        BiasZ = 0f;
        _stillTimer = 0;
        _accumGyroX = 0;
        _accumGyroY = 0;
        _accumGyroZ = 0;
        _accumCount = 0;
    }

    /// <summary>
    /// Processes a 6-axis motion state update and extracts filtered aim deltas and shake triggers.
    /// </summary>
    public void ProcessMotion(
        in MotionState motion,
        double deltaSeconds,
        out float aimDeltaYaw,
        out float aimDeltaPitch,
        out bool shakeDetected)
    {
        aimDeltaYaw = 0f;
        aimDeltaPitch = 0f;
        shakeDetected = false;

        if (deltaSeconds <= 0) deltaSeconds = 0.01; // default to 100Hz if zero

        // 1. Shake Detection (BetterJoy dynamic jerk)
        float accelMag = MathF.Sqrt(motion.AccelX * motion.AccelX + motion.AccelY * motion.AccelY + motion.AccelZ * motion.AccelZ);
        float dynamicG = MathF.Abs(accelMag - 1.0f);

        if (ShakeDetectionEnabled && dynamicG >= ShakeThresholdG)
        {
            var now = DateTime.UtcNow;
            if (now - _lastShakeTime >= ShakeCooldown)
            {
                _lastShakeTime = now;
                shakeDetected = true;
                OnShake?.Invoke();
            }
        }

        // 2. Auto-Rest Calibration
        float rawAngSpeed = MathF.Sqrt(motion.GyroX * motion.GyroX + motion.GyroY * motion.GyroY + motion.GyroZ * motion.GyroZ);
        bool isStill = rawAngSpeed <= StillVelocityThreshold && dynamicG <= StillGravityDeltaThreshold;

        if (AutoRestCalibrationEnabled)
        {
            if (isStill)
            {
                _stillTimer += deltaSeconds;
                _accumGyroX += motion.GyroX;
                _accumGyroY += motion.GyroY;
                _accumGyroZ += motion.GyroZ;
                _accumCount++;

                if (_stillTimer >= RestDurationRequiredSeconds && _accumCount > 10)
                {
                    // Calibrate bias to running average
                    BiasX = (float)(_accumGyroX / _accumCount);
                    BiasY = (float)(_accumGyroY / _accumCount);
                    BiasZ = (float)(_accumGyroZ / _accumCount);
                }
            }
            else
            {
                _stillTimer = 0;
                _accumGyroX = 0;
                _accumGyroY = 0;
                _accumGyroZ = 0;
                _accumCount = 0;
            }
        }

        if (!Enabled) return;

        // 3. Subtract Bias
        float gyroX = motion.GyroX - BiasX;
        float gyroY = motion.GyroY - BiasY;

        // 4. Micro-Tremor Filtering (JoyShockMapper tight deadzone curve)
        float speed2D = MathF.Sqrt(gyroX * gyroX + gyroY * gyroY);
        float factor;

        if (speed2D <= MinTremorThreshold)
        {
            factor = 0f;
        }
        else if (speed2D >= MaxTremorThreshold)
        {
            factor = 1.0f;
        }
        else
        {
            // Linear to quadratic blend between Min and Max threshold
            float t = (speed2D - MinTremorThreshold) / (MaxTremorThreshold - MinTremorThreshold);
            factor = t * t; // smooth ease-in eliminates jitter
        }

        float filteredGyroX = gyroX * factor;
        float filteredGyroY = gyroY * factor;

        // In landscape handheld orientation:
        // Yaw = horizontal aim (turning phone around vertical axis -> GyroY)
        // Pitch = vertical aim (tilting phone up/down -> GyroX)
        float yawRate = (InvertX ? -filteredGyroY : filteredGyroY) * SensitivityX;
        float pitchRate = (InvertY ? -filteredGyroX : filteredGyroX) * SensitivityY;

        aimDeltaYaw = (float)(yawRate * deltaSeconds);
        aimDeltaPitch = (float)(pitchRate * deltaSeconds);
    }

    /// <summary>
    /// Processes right thumbstick deflections with Jibb Smart's Flick Stick algorithm.
    /// Returns camera rotation delta in degrees for this frame.
    /// </summary>
    public float ProcessFlickStick(short stickX, short stickY, double deltaSeconds)
    {
        if (!FlickStickEnabled) return 0f;

        if (deltaSeconds <= 0) deltaSeconds = 0.01;

        float normX = stickX / 32767f;
        float normY = stickY / 32767f;
        float magnitude = MathF.Sqrt(normX * normX + normY * normY);

        float flickDeltaYaw = 0f;

        if (magnitude >= FlickStickDeadzone)
        {
            // Calculate stick angle in degrees:
            // 0 deg = Stick UP (forward)
            // 90 deg = Stick RIGHT
            // 180 deg = Stick DOWN (backward)
            // -90 deg = Stick LEFT
            float currentAngleDeg = MathF.Atan2(normX, -normY) * (180f / MathF.PI);

            if (!_isFlickEngaged)
            {
                // New flick started!
                _isFlickEngaged = true;
                _lastStickAngleDeg = currentAngleDeg;

                // Initiate snap turn towards the target angle
                _snapAngleTotalDeg = currentAngleDeg;
                _snapAngleRemainingDeg = currentAngleDeg;
                _snapElapsedSeconds = 0;
            }
            else
            {
                // Stick is being rolled along the outer rim
                float angleDelta = NormalizeAngle(currentAngleDeg - _lastStickAngleDeg);
                _lastStickAngleDeg = currentAngleDeg;
                flickDeltaYaw += angleDelta;
            }
        }
        else if (magnitude < FlickStickHysteresis)
        {
            // Stick returned to center
            _isFlickEngaged = false;
        }

        // Apply remaining snap turn over snap duration (smooth flick)
        if (MathF.Abs(_snapAngleRemainingDeg) > 0.01f)
        {
            _snapElapsedSeconds += deltaSeconds;
            if (_snapElapsedSeconds >= SnapDurationSeconds)
            {
                flickDeltaYaw += _snapAngleRemainingDeg;
                _snapAngleRemainingDeg = 0f;
            }
            else
            {
                float portion = (float)(deltaSeconds / (SnapDurationSeconds - _snapElapsedSeconds + deltaSeconds));
                float step = _snapAngleRemainingDeg * Math.Clamp(portion, 0f, 1f);
                flickDeltaYaw += step;
                _snapAngleRemainingDeg -= step;
            }
        }

        return flickDeltaYaw;
    }

    /// <summary>
    /// Helper to inject angular aim deltas directly into right analog stick coordinates.
    /// </summary>
    public static void InjectAimToRightStick(ref PadState padState, float aimDeltaYaw, float aimDeltaPitch, float stickMultiplier = 250f)
    {
        int newX = padState.ThumbRX + (int)(aimDeltaYaw * stickMultiplier);
        int newY = padState.ThumbRY + (int)(aimDeltaPitch * stickMultiplier);

        padState.ThumbRX = (short)Math.Clamp(newX, short.MinValue, short.MaxValue);
        padState.ThumbRY = (short)Math.Clamp(newY, short.MinValue, short.MaxValue);
    }

    /// <summary>
    /// Normalizes an angular delta into [-180, 180] degrees.
    /// </summary>
    public static float NormalizeAngle(float angleDeg)
    {
        while (angleDeg > 180f) angleDeg -= 360f;
        while (angleDeg < -180f) angleDeg += 360f;
        return angleDeg;
    }
}
