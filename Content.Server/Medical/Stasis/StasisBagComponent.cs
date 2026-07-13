using Robust.Shared.Containers;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.Medical.Stasis;

[RegisterComponent]
public sealed partial class StasisBagComponent : Component
{
    public const string BodyContainerId = "stasis_bag_body";

    public Container? BodyContainer;

    public float CurrentStasisFactor = 1.0f;

    public TimeSpan NextDegradationTime;

    public bool Spent;
}
