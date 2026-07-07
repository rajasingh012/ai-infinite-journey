using AiInfiniteJourney.Shared;
using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Process-scope singleton that holds the currently-active JourneyPlan and
/// the player's delivery progress (cargo loaded, delivery completed).
///
/// Lives as an autoload (see project.godot) so any scene can read the
/// current contract without needing to thread it through the scene tree.
///
/// Kept intentionally tiny: one plan, one contract, three booleans.
/// If the hackathon scope grows we add fields; we don't refactor this.
/// </summary>
public partial class JourneyState : Node
{
    public static JourneyState Instance { get; private set; } = null!;

    public JourneyPlan? Plan { get; private set; }
    public bool CargoLoaded { get; private set; }
    public bool DeliveryCompleted { get; private set; }

    [Signal] public delegate void PlanChangedEventHandler();
    [Signal] public delegate void CargoStateChangedEventHandler();

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }
        Instance = this;
    }

    public void SetPlan(JourneyPlan plan)
    {
        Plan = plan;
        CargoLoaded = false;
        DeliveryCompleted = false;
        EmitSignal(SignalName.PlanChanged);
        GD.Print($"[JourneyState] new plan: theme={plan.Theme}, " +
                 $"weather={plan.Weather}, time={plan.TimeOfDay}, " +
                 $"contract={(plan.Contract is null ? "NONE" : $"{plan.Contract.Cargo} for ${plan.Contract.Payout}")}");
    }

    public void MarkCargoLoaded()
    {
        if (CargoLoaded || DeliveryCompleted) return;
        CargoLoaded = true;
        EmitSignal(SignalName.CargoStateChanged);
        GD.Print("[JourneyState] cargo loaded");
    }

    public void MarkDelivered()
    {
        if (!CargoLoaded || DeliveryCompleted) return;
        DeliveryCompleted = true;
        EmitSignal(SignalName.CargoStateChanged);
        GD.Print("[JourneyState] delivery complete");
    }
}
