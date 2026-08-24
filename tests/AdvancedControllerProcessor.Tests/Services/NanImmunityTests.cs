using System;
using AdvancedControllerProcessor.Models;
using AdvancedControllerProcessor.Processing;
using AdvancedControllerProcessor.Services;
using Xunit;

namespace AdvancedControllerProcessor.Tests.Services;

/// <summary>
/// Regression tests for the NaN crash: MathF.Sign throws ArithmeticException
/// on NaN, which previously killed the input loop with
/// "Function does not accept floating point Not-a-Number values."
/// </summary>
public sealed class NanImmunityTests
{
    private static InputProcessingService NewService(bool enabled = true)
    {
        return new InputProcessingService
        {
            ProcessingEnabled = enabled,
            CurrentProfile = Profile.Default()
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Process_NaNStick_DoesNotThrowAndOutputIsFinite(bool enabled)
    {
        var svc = NewService(enabled);

        var result = svc.Process(new ControllerState
        {
            LeftStick = new StickState(float.NaN, float.PositiveInfinity),
            RightStick = new StickState(float.NegativeInfinity, 0.5f),
            L2 = float.NaN,
            R2 = 2f
        });

        Assert.All(new[]
        {
            result.LeftStick.X, result.LeftStick.Y,
            result.RightStick.X, result.RightStick.Y, result.L2, result.R2
        }, v => Assert.True(float.IsFinite(v), $"non-finite output {v}"));
    }

    [Fact]
    public void Process_AllProcessorsEnabled_NaNInputSurvives()
    {
        var profile = Profile.Default();
        profile.LeftStick.DeadzoneEnabled = true;
        profile.LeftStick.Deadzone = 0.1f;
        profile.LeftStick.ResponseCurve = "Aggressive";
        profile.LeftStick.SmoothingEnabled = true;
        profile.LeftStick.SmoothingAmount = 0.5f;
        profile.LeftStick.XSpeedMultiplier = 2f;

        var svc = new InputProcessingService
        {
            ProcessingEnabled = true,
            CurrentProfile = profile
        };

        var ex = Record.Exception(() => svc.Process(new ControllerState
        {
            LeftStick = new StickState(float.NaN, float.PositiveInfinity),
            RightStick = new StickState(0f, 0f)
        }));

        Assert.Null(ex);
    }

    [Fact]
    public void Smoothing_NanSample_DoesNotPoisonState()
    {
        var settings = new ProcessingSettings
        {
            SmoothingEnabled = true,
            SmoothingAmount = 0.5f
        };
        var smoother = new SmoothingProcessor();

        _ = smoother.Process(new StickState(0.5f, 0.5f), settings);
        var afterNan = smoother.Process(
            new StickState(float.NaN, float.NaN), settings);
        var recovered = smoother.Process(
            new StickState(0.4f, 0.6f), settings);

        Assert.Equal(0f, afterNan.X);
        Assert.True(float.IsFinite(recovered.X));
        Assert.True(float.IsFinite(recovered.Y));
    }

    [Fact]
    public void Deadzone_NaNInput_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            DeadzoneProcessor.ProcessRadial(
                new StickState(float.NaN, float.PositiveInfinity), 0.1f));

        Assert.Null(ex);
    }
}
