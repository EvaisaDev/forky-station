using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Medical.Wounds;

/// <summary>
///     Marks an entity (typically an external organ/limb) as woundable.
///     Wound entities are spawned as children and tracked here.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WoundableComponent : Component
{
    /// <summary>
    ///     List of wound entities on this woundable.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<EntityUid> Wounds = new();

    /// <summary>
    ///     Total accumulated damage from all wounds on this woundable.
    /// </summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier TotalDamage = new();

    /// <summary>
    ///     Total damage from tended wounds (for display purposes).
    /// </summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier TendedDamage = new();
}
