using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Medical.Wounds;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WoundableComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<EntityUid> Wounds = new();

    [DataField, AutoNetworkedField]
    public DamageSpecifier TotalDamage = new();

    [DataField, AutoNetworkedField]
    public DamageSpecifier TendedDamage = new();

    [DataField, AutoNetworkedField]
    public float Pain;
}
