using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Medical.IV;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class IVDripComponent : Component
{
    public const string BeakerSlotName = "beakerSlot";

    [DataField]
    public EntityUid? ConnectedPatient;

    [DataField, AutoNetworkedField]
    public bool Connected;

    [DataField, AutoNetworkedField]
    public IVDripMode Mode = IVDripMode.Inject;

    [DataField, AutoNetworkedField]
    public FixedPoint2 TransferRate = 1;

    [DataField]
    public List<FixedPoint2> AvailableTransferRates = new() { FixedPoint2.New(0), FixedPoint2.New(0.5f), FixedPoint2.New(1), FixedPoint2.New(2) };

    [DataField]
    public TimeSpan TransferTime = TimeSpan.FromSeconds(1);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField]
    public TimeSpan NextTransferTime = TimeSpan.Zero;

    [DataField, AutoNetworkedField]
    public string LastReagentColor = "#ffffff";
}

[Serializable, NetSerializable]
public enum IVDripMode : byte
{
    Inject,
    Draw
}

[Serializable, NetSerializable]
public enum IVDripVisuals : byte
{
    Connected,
    HasBeaker
}

[Serializable, NetSerializable]
public sealed partial class IVDripDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class IVDripDetachDoAfterEvent : SimpleDoAfterEvent;
