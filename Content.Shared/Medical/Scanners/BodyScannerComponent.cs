using Content.Shared.MedicalScanner;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Medical.Scanners;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BodyScannerComponent : Component
{
    public const string BodyContainerName = "scanner-body";

    [ViewVariables]
    public ContainerSlot BodyContainer = default!;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField]
    public TimeSpan NextUpdateTime = TimeSpan.Zero;

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
}

[Serializable, NetSerializable]
public enum BodyScannerVisuals : byte
{
    Status
}

[Serializable, NetSerializable]
public enum BodyScannerStatus : byte
{
    Off,
    Open,
    Red,
    Death,
    Green,
    Yellow,
}

[Serializable, NetSerializable]
public enum BodyScannerUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class BodyScannerUserMessage : BoundUserInterfaceMessage
{
    public HealthAnalyzerUiState Health;
    public bool HasOccupant;
    public bool Scanning;
    public List<string>? ScanData;

    public BodyScannerUserMessage(
        HealthAnalyzerUiState health,
        bool hasOccupant,
        bool scanning,
        List<string>? scanData)
    {
        Health = health;
        HasOccupant = hasOccupant;
        Scanning = scanning;
        ScanData = scanData;
    }
}

[Serializable, NetSerializable]
public sealed class BodyScannerScanMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class BodyScannerEjectMessage : BoundUserInterfaceMessage;


