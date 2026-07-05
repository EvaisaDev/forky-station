using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Medical.Wounds;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WoundComponent : Component
{
    [DataField, AutoNetworkedField]
    public DamageSpecifier Damage = new();

    [DataField(required: true), AutoNetworkedField]
    public FixedPoint2 MaximumDamage;

    [DataField, AutoNetworkedField]
    public EntityUid? ParentWoundable;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField, AutoNetworkedField]
    public TimeSpan CreatedAt;

    [DataField, AutoNetworkedField]
    public int Stage;

    [DataField, AutoNetworkedField]
    public int MaxStages = 5;

    [DataField, AutoNetworkedField]
    public float HealPerTick;

    [DataField, AutoNetworkedField]
    public bool IsSurgical;
}
