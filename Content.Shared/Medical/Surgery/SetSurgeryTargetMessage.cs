using Robust.Shared.Serialization;

namespace Content.Shared.Medical.Surgery;

[Serializable, NetSerializable]
public sealed class SetSurgeryTargetMessage : BoundUserInterfaceMessage
{
    public string TargetZone;

    public SetSurgeryTargetMessage(string targetZone)
    {
        TargetZone = targetZone;
    }
}
