using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Shared.Medical.Wounds;

/// <summary>
///     Raised on a woundable when damage needs to be aggregated from all wounds.
/// </summary>
[ByRefEvent]
public record struct GetWoundDamageEvent(DamageSpecifier Accumulator, DamageSpecifier? Tended);

/// <summary>
///     Raised to get total bleed amount from all wounds on a woundable.
/// </summary>
[ByRefEvent]
public record struct GetBleedLevelEvent(FixedPoint2 BleedAmount);

/// <summary>
///     Raised to get total pain from all wounds on a woundable.
/// </summary>
[ByRefEvent]
public record struct GetPainEvent(FixedPoint2 PainAmount, FixedPoint2 FreshPainAmount);

/// <summary>
///     Raised to find wounds with space for more damage.
/// </summary>
[ByRefEvent]
public record struct GetWoundsWithSpaceEvent(DamageSpecifier Damage);

/// <summary>
///     Raised to heal wounds.
/// </summary>
[ByRefEvent]
public record struct HealWoundsEvent(DamageSpecifier Damage);

/// <summary>
///     Raised to clamp wounds.
/// </summary>
[ByRefEvent]
public record struct ClampWoundsEvent(FixedPoint2 Amount);

/// <summary>
///     Raised when woundable damage changes.
/// </summary>
[ByRefEvent]
public record struct WoundableDamageChangedEvent;

/// <summary>
///     Raised to refresh wound state on a woundable.
/// </summary>
[ByRefEvent]
public record struct RefreshWoundsEvent;
