using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.MedicalScanner;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Medical.Sleeper;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class SleeperComponent : Component
{
    public const string BodyContainerName = "scanner-body";
    public const string InjectionBufferSolutionName = "injectionBuffer";
    public const string BeakerSlotName = "beakerSlot";

    [ViewVariables]
    public ContainerSlot BodyContainer = default!;

    [DataField]
    public TimeSpan BeakerTransferTime = TimeSpan.FromSeconds(2);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan NextInjectionTime = TimeSpan.Zero;

    [DataField]
    public TimeSpan UiUpdateInterval = TimeSpan.FromSeconds(1);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan NextUiUpdateTime = TimeSpan.Zero;

    [DataField]
    public FixedPoint2 BeakerTransferAmount = 1;

    [DataField]
    public float EntryDelay = 2f;

    [DataField]
    public bool Filtering;

    [DataField]
    public bool Pump;

    [DataField]
    public int StasisSetting = 1;

    [DataField]
    public List<int> StasisSettings = new() { 1, 2, 5, 10 };

    [DataField]
    public float SynthModifier = 1f;

    [DataField]
    public float PumpSpeed = 2f;

    [DataField]
    public List<string> AvailableChemicals = new() { "Inaprovaline", "Paracetamol", "Dylovene", "Dexalin" };

    [DataField]
    public List<string> UpgradeChemicals = new() { "Kelotane" };

    [DataField]
    public List<string> Upgrade2Chemicals = new() { "Hyronalin" };

    [DataField]
    public bool Locked;
}

[Serializable, NetSerializable]
public enum SleeperVisuals : byte
{
    ContainsEntity,
    IsOn
}

[Serializable, NetSerializable]
public enum SleeperUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class SleeperUserMessage : BoundUserInterfaceMessage
{
    public HealthAnalyzerUiState Health;
    public FixedPoint2? BeakerCapacity;
    public List<ReagentQuantity>? Beaker;
    public List<ReagentQuantity>? Injecting;
    public bool Filtering;
    public bool Pump;
    public int StasisSetting;
    public List<int> StasisSettings;
    public List<string> AvailableChemicals;
    public bool HasOccupant;
    public bool Locked;

    public SleeperUserMessage(
        HealthAnalyzerUiState health,
        FixedPoint2? beakerCapacity,
        List<ReagentQuantity>? beaker,
        List<ReagentQuantity>? injecting,
        bool filtering,
        bool pump,
        int stasisSetting,
        List<int> stasisSettings,
        List<string> availableChemicals,
        bool hasOccupant,
        bool locked)
    {
        Health = health;
        BeakerCapacity = beakerCapacity;
        Beaker = beaker;
        Injecting = injecting;
        Filtering = filtering;
        Pump = pump;
        StasisSetting = stasisSetting;
        StasisSettings = stasisSettings;
        AvailableChemicals = availableChemicals;
        HasOccupant = hasOccupant;
        Locked = locked;
    }
}

[Serializable, NetSerializable]
public sealed class SleeperToggleFilterMessage : BoundUserInterfaceMessage
{
    public bool Filtering;
    public SleeperToggleFilterMessage(bool filtering) => Filtering = filtering;
}

[Serializable, NetSerializable]
public sealed class SleeperTogglePumpMessage : BoundUserInterfaceMessage
{
    public bool Pump;
    public SleeperTogglePumpMessage(bool pump) => Pump = pump;
}

[Serializable, NetSerializable]
public sealed class SleeperSetStasisMessage : BoundUserInterfaceMessage
{
    public int StasisSetting;
    public SleeperSetStasisMessage(int stasisSetting) => StasisSetting = stasisSetting;
}

[Serializable, NetSerializable]
public sealed class SleeperInjectChemicalMessage : BoundUserInterfaceMessage
{
    public string Chemical;
    public FixedPoint2 Amount;
    public SleeperInjectChemicalMessage(string chemical, FixedPoint2 amount)
    {
        Chemical = chemical;
        Amount = amount;
    }
}

[Serializable, NetSerializable]
public sealed class SleeperEjectPatientMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class SleeperEjectBeakerMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed partial class SleeperDragFinished : SimpleDoAfterEvent;
