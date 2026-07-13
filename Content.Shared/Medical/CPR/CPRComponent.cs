using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Medical.CPR;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CPRComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField, AutoNetworkedField]
    public float CardiacOutputModifier = 0.8f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField]
    public TimeSpan ExpiryTime;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(3);

    [DataField, AutoNetworkedField]
    public EntityUid? Performer;
}
