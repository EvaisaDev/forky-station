using Robust.Shared.GameStates;

namespace Content.Shared.Body.Components;

/// <summary>
/// Tracks blood oxygenation, pulse rate, and cardiac arrest state.
/// Updated each tick by BloodOxygenationSystem.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BloodOxygenationComponent : Component
{
    /// <summary>
    /// Current blood oxygenation percentage (0.0 to 1.0).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Oxygenation = 1.0f;

    /// <summary>
    /// Current pulse rate in BPM (beats per minute).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float PulseRate = 72f;

    /// <summary>
    /// Whether the entity is in cardiac arrest.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CardiacArrest;

    /// <summary>
    /// Base pulse rate for this species.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BasePulse = 72f;

    /// <summary>
    /// Accumulated brain damage from low oxygenation as FixedPoint2
    /// </summary>
    [DataField, AutoNetworkedField]
    public float AccumulatedBrainDamage;
}
