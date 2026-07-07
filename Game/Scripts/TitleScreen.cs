using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Bare-bones "press Enter to start a new journey" handler.
///
/// Day 1 goal: prove the wire is live end-to-end. No HUD yet — just an
/// input action that fires the API call, stores the result in
/// JourneyState, and prints a one-line summary to the console so we
/// can confirm during ad-hoc verification.
///
/// Day 2 will replace the print statements with a CanvasLayer HUD
/// showing the contract (pickup → dropoff, cargo, payout).
/// </summary>
public partial class TitleScreen : Node
{
    [Export] public NodePath JourneyApiPath { get; set; } = "../../JourneyApi";
    [Export] public string DefaultPrompt { get; set; } = "an interesting drive through a varied landscape";

    private JourneyApi? _api;
    private bool _busy;

    public override void _Ready()
    {
        _api = GetNodeOrNull<JourneyApi>(JourneyApiPath);
        if (_api is null)
        {
            GD.PushWarning($"[TitleScreen] JourneyApi not found at {JourneyApiPath}");
        }
        GD.Print("[TitleScreen] ready. Press Enter to start a new journey.");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_busy) return;
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode != Key.Enter && key.Keycode != Key.KpEnter) return;

        _ = StartJourney();
    }

    private async System.Threading.Tasks.Task StartJourney()
    {
        if (_api is null) return;
        _busy = true;
        GD.Print($"[TitleScreen] requesting journey: \"{DefaultPrompt}\"");
        var plan = await _api.DirectAsync(DefaultPrompt);
        _busy = false;

        if (plan is null)
        {
            GD.PushWarning("[TitleScreen] journey request failed; staying on title screen");
            return;
        }

        JourneyState.Instance.SetPlan(plan);
    }
}
