using Robust.Shared.GameStates;

namespace Content.Shared.Medical.Wounds;

/// <summary>
///     Tracks germ/infection level on a wound (0-100).
///     Spaceacillin reduces this. Untended environments increase it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WoundGermComponent : Component
{
    [DataField, AutoNetworkedField]
    public float GermLevel;
}
