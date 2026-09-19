using System;
using System.Threading;
using OmniPadServer.App;
using OmniPadServer.Core;
using Xunit;

namespace OmniPadServer.Tests;

public class GyroMathTests
{
    [Fact]
    public void MicroTremorFilter_SuppressesLowVelocityJitter()
    {
        var engine = new GyroAimEngine();
        
        // Tremor below 3.5 deg/s
        var tremorMotion = new MotionState
        {
            AccelY = 1.0f,
            GyroX = 1.5f,
            GyroY = -2.0f
        };

        engine.ProcessMotion(in tremorMotion, 0.01, out float aimDeltaYaw, out float aimDeltaPitch, out _);
        
        Assert.Equal(0f, aimDeltaYaw);
        Assert.Equal(0f, aimDeltaPitch);
    }

    [Fact]
    public void MicroTremorFilter_PassesHighVelocityMotion()
    {
        var engine = new GyroAimEngine();

        // Deliberate turn at 30 deg/s
        var fastMotion = new MotionState
        {
            AccelY = 1.0f,
            GyroX = 30.0f,
            GyroY = -40.0f
        };

        engine.ProcessMotion(in fastMotion, 0.01, out float aimDeltaYaw, out float aimDeltaPitch, out _);

        // At 30 deg/s and deltaSeconds = 0.01s, full delta is passed through
        Assert.NotEqual(0f, aimDeltaYaw);
        Assert.NotEqual(0f, aimDeltaPitch);
        Assert.True(MathF.Abs(aimDeltaYaw) > 0.1f);
    }

    [Fact]
    public void AutoRestCalibration_DetectsStillnessAndCancelsBias()
    {
        var engine = new GyroAimEngine
        {
            RestDurationRequiredSeconds = 0.2 // faster test
        };

        // Simulated drift bias of +1.0 deg/s on X and -0.8 deg/s on Y
        var restingMotionWithDrift = new MotionState
        {
            AccelY = 1.0f,
            GyroX = 1.0f,
            GyroY = -0.8f,
            GyroZ = 0.2f
        };

        // Feed 30 frames at 100Hz = 0.3s of still motion
        for (int i = 0; i < 30; i++)
        {
            engine.ProcessMotion(in restingMotionWithDrift, 0.01, out _, out _, out _);
        }

        // Biases should now be calibrated close to +1.0 and -0.8
        Assert.InRange(engine.BiasX, 0.9f, 1.1f);
        Assert.InRange(engine.BiasY, -0.9f, -0.7f);

        // Subsequent stillness motion with drift subtracted should yield 0
        engine.ProcessMotion(in restingMotionWithDrift, 0.01, out float aimDeltaYaw, out float aimDeltaPitch, out _);
        Assert.Equal(0f, aimDeltaYaw);
        Assert.Equal(0f, aimDeltaPitch);
    }

    [Fact]
    public void ShakeDetector_TriggersOnAccelerationSpike()
    {
        var engine = new GyroAimEngine();
        bool shakeTriggered = false;
        engine.OnShake += () => shakeTriggered = true;

        // Normal resting gravity (1.0G on Y)
        var calmMotion = new MotionState { AccelY = 1.0f };
        engine.ProcessMotion(in calmMotion, 0.01, out _, out _, out bool detectedCalm);
        Assert.False(detectedCalm);
        Assert.False(shakeTriggered);

        // Sudden 2.8G jerk spike
        var jerkMotion = new MotionState { AccelX = 2.0f, AccelY = 2.0f, AccelZ = 0.5f }; // |A| = 2.87G, dynamic = 1.87G > 1.4G
        engine.ProcessMotion(in jerkMotion, 0.01, out _, out _, out bool detectedJerk);
        Assert.True(detectedJerk);
        Assert.True(shakeTriggered);
    }

    [Fact]
    public void FlickStick_SnapsOnDeflection()
    {
        var engine = new GyroAimEngine
        {
            FlickStickEnabled = true,
            SnapDurationSeconds = 0.01f // instant snap for unit test
        };

        // 1. Stick inside deadzone: returns 0
        float deltaInside = engine.ProcessFlickStick(5000, 5000, 0.01);
        Assert.Equal(0f, deltaInside);

        // 2. Flick straight backward (stick DOWN): X = 0, Y = 32767
        // In screen coordinates: forward is -Y, backward is +Y.
        // atan2(0, -32767) = 180 degrees
        float snapBackward = engine.ProcessFlickStick(0, 32767, 0.02);
        Assert.InRange(snapBackward, 179f, 181f);

        // 3. Stick held at same position: no extra flick rotation
        float heldStill = engine.ProcessFlickStick(0, 32767, 0.01);
        Assert.InRange(heldStill, -0.01f, 0.01f);

        // 4. Roll stick from DOWN (180 deg) to RIGHT (90 deg): X = 32767, Y = 0
        // Delta should be -90 deg
        float rolledRight = engine.ProcessFlickStick(32767, 0, 0.01);
        Assert.InRange(rolledRight, -91f, -89f);
    }

    [Fact]
    public void NormalizeAngle_WrapsProperly()
    {
        Assert.Equal(0f, GyroAimEngine.NormalizeAngle(0f));
        Assert.Equal(90f, GyroAimEngine.NormalizeAngle(90f));
        Assert.Equal(-90f, GyroAimEngine.NormalizeAngle(-90f));
        Assert.Equal(180f, GyroAimEngine.NormalizeAngle(180f));
        Assert.Equal(-170f, GyroAimEngine.NormalizeAngle(190f));
        Assert.Equal(170f, GyroAimEngine.NormalizeAngle(-190f));
    }
}
