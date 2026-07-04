using Content.Shared.Body;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Surgery;

/// <summary>
///     Stores the player's currently selected surgery target zone.
///     Set via chat command or UI. Defaults to Torso.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SurgeryTargetComponent : Component
{
    /// <summary>
    ///     The organ category to target (e.g., "Torso", "Head", "ArmLeft", etc.).
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<OrganCategoryPrototype> TargetZone = "Torso";
}
