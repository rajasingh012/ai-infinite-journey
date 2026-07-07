using System;
using AiInfiniteJourney.Shared;
using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Headless smoke test for JourneyApi. Loads JourneyApi, calls DirectAsync
/// with a fixed prompt, prints the parsed result, then quits. Used by
/// /tmp/hermes-verify-aij-godot.py for ad-hoc verification.
///
/// Not a unit test — Godot's test runner doesn't fit our setup. This
/// is a one-shot harness; delete it after the hackathon.
/// </summary>
public partial class JourneyApiSmokeTest : Node
{
    [Export] public string Prompt { get; set; } = "a calm evening drive through farmland";

    public override async void _Ready()
    {
        var api = new JourneyApi
        {
            BaseUrl = "http://localhost:5080",
            EndpointPath = "/api/world/direct",
        };
        AddChild(api);

        GD.Print($"[SmokeTest] prompt=\"{Prompt}\"");
        try
        {
            var plan = await api.DirectAsync(Prompt);
            if (plan is null)
            {
                GD.Print("[SmokeTest] RESULT: plan was null");
                GetTree().Quit(1);
                return;
            }

            var c = plan.Contract;
            GD.Print($"[SmokeTest] RESULT: theme={plan.Theme} " +
                     $"weather={plan.Weather} time={plan.TimeOfDay} " +
                     $"landmarks=[{string.Join(",", plan.Landmarks)}] " +
                     $"contract={(c is null ? "NONE" : $"{c.PickupLabel}→{c.DropoffLabel} {c.Cargo} ${c.Payout}")}");

            // Verify the contract made the round trip
            if (c is null)
            {
                GD.Print("[SmokeTest] FAIL: contract is null");
                GetTree().Quit(2);
                return;
            }
            if (string.IsNullOrEmpty(c.Cargo) || c.Payout <= 0)
            {
                GD.Print($"[SmokeTest] FAIL: contract fields look wrong: {c.Cargo} ${c.Payout}");
                GetTree().Quit(3);
                return;
            }

            GD.Print("[SmokeTest] PASS");
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.Print($"[SmokeTest] EXCEPTION: {e.GetType().Name}: {e.Message}");
            GetTree().Quit(4);
        }
    }
}
