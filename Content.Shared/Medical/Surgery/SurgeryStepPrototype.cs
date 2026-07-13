using Content.Shared.Body.Organs;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical.Surgery;

[Flags, Serializable, NetSerializable]
public enum SurgeryStepFlags : byte
{
    None = 0,
    NoRobotic = 1 << 0,
    NoCrystal = 1 << 1,
    NoStump = 1 << 2,
    NoFlesh = 1 << 3,
    NeedsIncision = 1 << 4,
    NeedsRetracted = 1 << 5,
    NeedsEncasement = 1 << 6,
}

[Prototype]
public sealed partial class SurgeryStepPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField(required: true)]
    public string Action { get; private set; } = string.Empty;

    [DataField]
    public string Name { get; private set; } = string.Empty;

    [DataField]
    public string Description { get; private set; } = string.Empty;

    [DataField]
    public Dictionary<EntProtoId, int> AllowedTools { get; private set; } = new();

    [DataField]
    public SurgeryStepFlags RequiredFlags { get; private set; }

    [DataField]
    public float MinDuration { get; private set; } = 3.0f;

    [DataField]
    public float MaxDuration { get; private set; } = 5.0f;

    /// <summary>
    /// Whether this step can spread infection.
    /// </summary>
    [DataField]
    public bool CanInfect { get; private set; }

    /// <summary>
    /// How much blood gets on the surgeon. 1 = hands, 2 = full body.
    /// </summary>
    [DataField]
    public int BloodLevel { get; private set; }

    /// <summary>
    /// Shock level added to patient when this step begins.
    /// </summary>
    [DataField]
    public float ShockLevel { get; private set; }

    /// <summary>
    /// If true, this step requires a stable surface (optable/bed) for best results.
    /// </summary>
    [DataField]
    public bool Delicate { get; private set; }

    /// <summary>
    /// Whether strict size/orientation matching is needed for access.
    /// </summary>
    [DataField]
    public bool StrictAccessRequirement { get; private set; } = true;
}
