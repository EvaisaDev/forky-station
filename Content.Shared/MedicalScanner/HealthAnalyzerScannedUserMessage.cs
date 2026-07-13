using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared.MedicalScanner;

[Serializable, NetSerializable]
public sealed class HealthAnalyzerScannedUserMessage : BoundUserInterfaceMessage
{
    public HealthAnalyzerUiState State;

    public HealthAnalyzerScannedUserMessage(HealthAnalyzerUiState state)
    {
        State = state;
    }
}

[Serializable, NetSerializable]
public struct LimbScanData
{
    public string Name;
    public FixedPoint2 BruteDamage;
    public FixedPoint2 BurnDamage;
    public bool Fractured;
    public bool Bleeding;
}

[Serializable, NetSerializable]
public struct ReagentScanData
{
    public string Name;
    public FixedPoint2 Quantity;
}

[Serializable, NetSerializable]
public struct HealthAnalyzerUiState
{
    public readonly NetEntity? TargetEntity;
    public float Temperature;
    public float BloodLevel;
    public bool? ScanMode;
    public bool? Bleeding;
    public bool? Unrevivable;

    // Baystation fields
    public string BrainActivity; // "Normal", "Fading", "None"
    public float PulseRate;
    public float BloodOxygenation;
    public int TotalDamage;
    public List<LimbScanData> Limbs;
    public List<ReagentScanData> Reagents;
    public bool HasFractures;
    public bool HasInternalBleeding;
    public bool HasOrganFailure;

    public HealthAnalyzerUiState()
    {
        BrainActivity = "Normal";
        Limbs = new();
        Reagents = new();
    }

    public HealthAnalyzerUiState(
        NetEntity? targetEntity,
        float temperature,
        float bloodLevel,
        bool? scanMode,
        bool? bleeding,
        bool? unrevivable)
    {
        TargetEntity = targetEntity;
        Temperature = temperature;
        BloodLevel = bloodLevel;
        ScanMode = scanMode;
        Bleeding = bleeding;
        Unrevivable = unrevivable;
        BrainActivity = "Normal";
        PulseRate = 0;
        BloodOxygenation = 0;
        Limbs = new();
        Reagents = new();
    }
}
