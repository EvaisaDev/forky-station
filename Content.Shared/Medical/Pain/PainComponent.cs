using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Medical.Pain;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PainComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan NextUpdate;

    [DataField, AutoNetworkedField]
    public float ShockLevel;

    [DataField, AutoNetworkedField]
    public float PainkillerLevel;

    [DataField, AutoNetworkedField]
    public float FreshPain;

    [DataField]
    public float FreshPainDecay = 0.05f;
}
