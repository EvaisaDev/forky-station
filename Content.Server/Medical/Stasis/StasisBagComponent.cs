using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.Medical.Stasis;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class StasisBagComponent : Component
{
    public const string BodyContainerId = "stasis_bag_body";

    [DataField]
    public Container? BodyContainer;

    [DataField, AutoNetworkedField]
    public float CurrentStasisFactor = 1.0f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextDegradationTime;

    [DataField, AutoNetworkedField]
    public bool Spent;
}
