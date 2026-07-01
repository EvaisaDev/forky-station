using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Medical.Wounds;

/// <summary>
///     Base component for all wound entities.
///     Each wound represents a localized injury on a woundable body part.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WoundComponent : Component
{
    /// <summary>
    ///     The amount of damage this wound represents.
    /// </summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier Damage = new();

    /// <summary>
    ///     Maximum damage this wound can absorb before it's maxed out.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public FixedPoint2 MaximumDamage;

    /// <summary>
    ///     The woundable entity (limb) this wound belongs to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ParentWoundable;

    /// <summary>
    ///     When this wound was created.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField, AutoNetworkedField]
    public TimeSpan CreatedAt;
}
