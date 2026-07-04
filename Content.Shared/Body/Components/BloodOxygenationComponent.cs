using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Body.Components;

[RegisterComponent, AutoGenerateComponentState]
public sealed partial class BloodOxygenationComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan NextUpdate;

    [DataField, AutoNetworkedField]
    public float Oxygenation = 1.0f;

    [DataField, AutoNetworkedField]
    public int PulseLevel = 2;

    [DataField, AutoNetworkedField]
    public float PulseRate = 72f;

    [DataField, AutoNetworkedField]
    public bool CardiacArrest;

    [DataField, AutoNetworkedField]
    public float BasePulse = 72f;

    [DataField, AutoNetworkedField]
    public float AccumulatedBrainDamage;
}
