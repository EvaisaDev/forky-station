using Robust.Shared.GameStates;

namespace Content.Shared.Body.Components;

/// <summary>
/// Tracks heart condition. Efficiency 0-1 determines how well the heart pumps blood.
/// 1.0 = healthy, 0.0 = total failure (cardiac arrest).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HeartConditionComponent : Component
{
    /// <summary>
    /// How efficiently the heart pumps blood (0.0 to 1.0).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Efficiency = 1.0f;

    /// <summary>
    /// Whether the heart is currently beating. False means cardiac arrest.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Beating = true;
}
