using AdvancedControllerProcessor.Models;
using AdvancedControllerProcessor.Services;
using AdvancedControllerProcessor.ViewModels;
using Xunit;

namespace AdvancedControllerProcessor.Tests.Services;

/// <summary>
/// Reproduces the exact data flow the app uses:
/// UI ViewModels -> MainViewModel.OnStickSettingsChanged equivalent ->
/// InputProcessingService -> virtual output state.
/// Verifies that user-visible settings (curve, speed, deadzone) actually
/// reach the processed output.
/// </summary>
public class SettingsFlowTests
{
    private static (LeftStickViewModel Left, RightStickViewModel Right, InputProcessingService Svc)
        CreateWiredPipeline()
    {
        var left = new LeftStickViewModel();
        var right = new RightStickViewModel();
        var svc = new InputProcessingService();

        // Mirror of MainViewModel.OnStickSettingsChanged
        var profile = Profile.Default();
        void OnStickSettingsChanged()
        {
            profile.LeftStick = left.ToSettings();
            profile.RightStick = right.ToSettings();
            svc.CurrentProfile = profile;
        }

        // Mirror of MainViewModel constructor wiring
        left.OnChanged = OnStickSettingsChanged;
        right.OnChanged = OnStickSettingsChanged;

        // Mirror of startup: LoadProfile -> LoadFrom (suppressed callbacks)
        left.LoadFrom(profile.LeftStick);
        right.LoadFrom(profile.RightStick);

        return (left, right, svc);
    }

    [Fact]
    public void AggressiveCurve_ChangedInUi_AppliesToOutput()
    {
        var (left, _, svc) = CreateWiredPipeline();

        svc.ProcessingEnabled = true;   // auto-start on connect
        left.ResponseCurve = "Aggressive"; // user picks Aggressive in the combo

        var raw = new ControllerState { LeftStick = new StickState(0.5f, 0f) };
        var processed = svc.Process(raw);

        float expected = MathF.Pow(0.5f, 0.7f); // ~0.615
        Assert.Equal(expected, processed.LeftStick.X, 3);
        Assert.True(processed.LeftStick.X > 0.55f,
            $"Aggressive curve not applied: X={processed.LeftStick.X}");
    }

    [Fact]
    public void SpeedMultiplier_ChangedInUi_AppliesToOutput()
    {
        var (left, _, svc) = CreateWiredPipeline();

        svc.ProcessingEnabled = true;
        left.XSpeed = 1.5f;
        left.YSpeed = 1.5f;

        var raw = new ControllerState { LeftStick = new StickState(0.5f, 0.4f) };
        var processed = svc.Process(raw);

        Assert.Equal(0.75f, processed.LeftStick.X, 3);
        Assert.Equal(0.6f, processed.LeftStick.Y, 3);
    }

    [Fact]
    public void Deadzone_ChangedInUi_AppliesToOutput()
    {
        var (left, _, svc) = CreateWiredPipeline();

        svc.ProcessingEnabled = true;
        left.DeadzoneEnabled = true;
        left.Deadzone = 0.2f;

        var inside = new ControllerState { LeftStick = new StickState(0.1f, 0f) };
        var outside = new ControllerState { LeftStick = new StickState(0.6f, 0f) };

        Assert.Equal(0f, svc.Process(inside).LeftStick.X);
        Assert.True(svc.Process(outside).LeftStick.X > 0.4f);
    }

    [Fact]
    public void ProcessingDisabled_PassesThrough_EvenWithAggressiveSelected()
    {
        var (left, _, svc) = CreateWiredPipeline();

        svc.ProcessingEnabled = false; // toggle OFF
        left.ResponseCurve = "Aggressive";

        var raw = new ControllerState { LeftStick = new StickState(0.5f, 0f) };
        var processed = svc.Process(raw);

        Assert.Equal(0.5f, processed.LeftStick.X, 5); // untouched
    }

    [Fact]
    public void RightStick_ProcessingEnabledWithAggressiveCurve_AppliesToOutput()
    {
        var (_, right, svc) = CreateWiredPipeline();

        svc.ProcessingEnabled = true;
        right.ProcessingEnabled = true;                    // user checks "Enable Right Stick Processing"
        right.Processing.ResponseCurve = "Aggressive";     // user picks a curve in the Right tab

        var raw = new ControllerState { RightStick = new StickState(0.5f, 0f) };
        var processed = svc.Process(raw);

        float expected = MathF.Pow(0.5f, 0.7f);
        Assert.Equal(expected, processed.RightStick.X, 3);
    }

    [Fact]
    public void RightStick_ProcessingEnabledWithDefaults_IsLinearPassValue()
    {
        var (_, right, svc) = CreateWiredPipeline();

        svc.ProcessingEnabled = true;
        right.ProcessingEnabled = true; // enabled but inner settings untouched (linear, 1.0x)

        var raw = new ControllerState { RightStick = new StickState(0.5f, -0.25f) };
        var processed = svc.Process(raw);

        Assert.Equal(0.5f, processed.RightStick.X, 3);
        Assert.Equal(-0.25f, processed.RightStick.Y, 3);
    }

    [Fact]
    public void RightStick_Disabled_PassesThroughEvenWithCurvedInnerSettings()
    {
        var (_, right, svc) = CreateWiredPipeline();

        svc.ProcessingEnabled = true;
        right.Processing.ResponseCurve = "Aggressive"; // configured...
        right.ProcessingEnabled = false;               // ...but processing off

        var raw = new ControllerState { RightStick = new StickState(0.5f, 0f) };
        var processed = svc.Process(raw);

        Assert.Equal(0.5f, processed.RightStick.X, 5);
    }

    [Fact]
    public void RightStick_SettingsRoundTrip_PreservesCurveSelection()
    {
        var (_, right, _) = CreateWiredPipeline();

        right.ProcessingEnabled = true;
        right.Processing.ResponseCurve = "Soft";
        right.Processing.XSpeed = 1.4f;
        var saved = right.ToSettings();

        var reloaded = new RightStickViewModel();
        reloaded.LoadFrom(saved);

        Assert.True(reloaded.ProcessingEnabled);
        Assert.Equal("Soft", reloaded.Processing.ResponseCurve);
        Assert.Equal(1.4f, reloaded.Processing.XSpeed);

        var svc = new InputProcessingService { ProcessingEnabled = true, CurrentProfile = Profile.Default() };
        svc.CurrentProfile.RightStick = reloaded.ToSettings();
        var raw = new ControllerState { RightStick = new StickState(0.5f, 0f) };
        // Pipeline order: curve first (soft), then speed multiplier.
        float curvedThenScaled = MathF.Pow(0.5f, 1.5f) * 1.4f;
        Assert.Equal(curvedThenScaled, svc.Process(raw).RightStick.X, 3);
    }
}
