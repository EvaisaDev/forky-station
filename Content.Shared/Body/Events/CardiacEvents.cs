using Content.Shared.Body.Components;

namespace Content.Shared.Body.Events;

/// <summary>
/// Raised when an entity enters or leaves cardiac arrest.
/// </summary>
[ByRefEvent]
public readonly record struct CardiacArrestEvent(EntityUid Body, bool Arrested);

/// <summary>
/// Raised when an entity's heart stops or starts.
/// </summary>
[ByRefEvent]
public readonly record struct HeartStatusEvent(EntityUid Heart, bool Beating);
