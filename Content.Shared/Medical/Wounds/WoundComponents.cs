using Content.Shared.FixedPoint;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Wounds;

/// <summary>
///     Wound that bleeds, contributing to bloodstream bleed amount.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BleedingWoundComponent : Component
{
    /// <summary>
    ///     Base rate of bleeding from this wound.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 BaseBleedAmount;

    /// <summary>
    ///     Current bleed contribution (scales with wound damage).
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 CurrentBleedAmount;

    /// <summary>
    ///     How much bleed timer remains before wound stops bleeding naturally.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BleedTimer;
}

/// <summary>
///     Wound that can be bandaged/sutured to stop bleeding.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TendableWoundComponent : Component
{
    /// <summary>
    ///     Whether this wound has been tended (bandaged/sutured).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Tended;
}

/// <summary>
///     Wound that can be clamped to temporarily stop bleeding.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ClampableWoundComponent : Component
{
    /// <summary>
    ///     Whether this wound is currently clamped.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Clamped;
}

/// <summary>
///     Wound that can heal over time when treated.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HealableWoundComponent : Component
{
    /// <summary>
    ///     How much damage this wound heals per tick.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 HealPerTick;

    /// <summary>
    ///     Whether this wound is currently healing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Healing;
}

/// <summary>
///     Wound that causes pain.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PainfulWoundComponent : Component
{
    /// <summary>
    ///     Pain contribution from this wound (before painkillers).
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 PainAmount;

    /// <summary>
    ///     Fresh pain (decays quickly, spikes on initial injury).
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 FreshPainAmount;

    /// <summary>
    ///     Rate at which fresh pain decays per second.
    /// </summary>
    [DataField]
    public double FreshPainDecayPerSecond = 0.05;
}

/// <summary>
///     Wound with text descriptions for examine/analyzer output.
/// </summary>
[RegisterComponent]
public sealed partial class WoundDescriptionComponent : Component
{
    /// <summary>
    ///     Descriptions keyed by damage threshold (highest matching threshold used).
    /// </summary>
    [DataField(required: true)]
    public SortedDictionary<FixedPoint2, string> Descriptions = new();
}
