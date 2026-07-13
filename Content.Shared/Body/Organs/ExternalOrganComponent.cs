using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Body.Organs;

/// <summary>
///Baystation-style external organ (limb) component.
///Tracks per-limb brute/burn damage, status flags, and surgery state.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExternalOrganComponent : Component
{
    /// <summary>
    /// Total brute damage on this limb (Blunt + Slash + Piercing).
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 BruteDamage;

    /// <summary>
    /// Total burn damage on this limb (Heat + Cold + Shock + Caustic).
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 BurnDamage;

    /// <summary>
    /// Maximum damage this limb can sustain before being considered destroyed/dead.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 MaxDamage = 100;

    /// <summary>
    /// Damage threshold above which this limb becomes broken (fractured).
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 MinBrokenDamage = 60;

    /// <summary>
    /// Bitfield of organ status flags.
    /// </summary>
    [DataField, AutoNetworkedField]
    public OrganStatusFlags Status;

    /// <summary>
    /// Bitfield of limb capability flags.
    /// </summary>
    [DataField, AutoNetworkedField]
    public LimbFlags Flags;

    /// <summary>
    /// Whether this limb is dislocated.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Dislocated;

    /// <summary>
    ///     Surgery state: tracks incision/clamping/retraction/encasement progress.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SurgeryStage SurgeryStage;

    /// <summary>
    ///     Bone repair progress (0-3, 0=none, 1=glued, 2=set, 3=finished).
    /// </summary>
    [DataField, AutoNetworkedField]
    public int BoneRepairStage;

    /// <summary>
    ///     Name of the encased bone structure (e.g. "ribcage", "skull").
    ///     If set, this limb requires sawing through bone to access internals.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? Encased;

    /// <summary>
    ///     Whether this limb is disfigured (scarred/mangled beyond recognition).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Disfigured;
}

/// <summary>
/// Bitfield flags for organ/limb status.
/// </summary>
[Flags, Serializable, NetSerializable]
public enum OrganStatusFlags : byte
{
    None = 0,
    Broken = 1 << 0,       // Bone is fractured
    Bleeding = 1 << 1,     // Limb is bleeding (open wound)
    Dead = 1 << 2,         // Limb tissue is necrotic/dead
    CutAway = 1 << 3,      // Limb has been surgically opened
    ArteryCut = 1 << 4,    // Artery has been severed
    TendonCut = 1 << 5,    // Tendon has been severed
    Robotic = 1 << 6,      // Limb is robotic/prosthetic
    Brittle = 1 << 7,      // Robotic limb is brittle
}

/// <summary>
/// Bitfield flags for limb capabilities.
/// </summary>
[Flags, Serializable, NetSerializable]
public enum LimbFlags : byte
{
    None = 0,
    CanAmputate = 1 << 0,  // Can be amputated
    CanBreak = 1 << 1,     // Can be fractured
    CanGrasp = 1 << 2,     // Can grasp items (hands)
    CanStand = 1 << 3,     // Can support standing (legs)
    HasTendon = 1 << 4,    // Has tendons that can be severed
}

/// <summary>
/// Surgery stage for an external organ.
/// </summary>
[Serializable, NetSerializable]
public enum SurgeryStage : byte
{
    None,
    Incised,    // Skin cut open (scalpel used)
    Clamped,    // Bleeders clamped (hemostat used)
    Retracted,  // Skin retracted (retractors used)
    Encased,    // Bone/ribcage opened (saw used)
}
