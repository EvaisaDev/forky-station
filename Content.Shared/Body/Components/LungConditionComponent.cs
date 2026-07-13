using Robust.Shared.GameStates;

namespace Content.Shared.Body.Components;

/// <summary>
/// Tracks lung condition. Efficiency 0-1 determines how well the lungs oxygenate blood.
/// 1.0 = healthy, 0.0 = total failure.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class LungConditionComponent : Component
{
    /// <summary>
    /// How efficiently the lungs oxygenate blood.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Efficiency = 1.0f;
}
