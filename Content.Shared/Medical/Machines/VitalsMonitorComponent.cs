using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Medical.Machines;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VitalsMonitorComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? ConnectedPatient;

    [DataField, AutoNetworkedField]
    public bool Connected;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField]
    public TimeSpan NextUpdateTime = TimeSpan.Zero;

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);

    [DataField, AutoNetworkedField]
    public float PulseRate;

    [DataField, AutoNetworkedField]
    public float BloodOxygenation;

    [DataField, AutoNetworkedField]
    public string BrainActivity = "Normal";

    [DataField, AutoNetworkedField]
    public string BreathingStatus = "Normal";

    [DataField, AutoNetworkedField]
    public bool HasCardiacArrest;

    [DataField, AutoNetworkedField]
    public bool HasBrainDamage;

    [DataField, AutoNetworkedField]
    public bool HasBreathingProblem;
}

[Serializable, NetSerializable]
public enum VitalsMonitorVisuals : byte
{
    PulseStatus,
    BrainStatus,
    BreathingStatus,
    Powered
}

[Serializable, NetSerializable]
public enum VitalsPulseStatus : byte
{
    None,
    Normal,
    Fast,
    Threading,
    Flatline
}

[Serializable, NetSerializable]
public enum VitalsBrainStatus : byte
{
    None,
    Normal,
    Weak,
    Critical,
    Warning
}

[Serializable, NetSerializable]
public enum VitalsBreathingStatus : byte
{
    None,
    Normal,
    Shallow,
    NotBreathing,
    Warning
}
