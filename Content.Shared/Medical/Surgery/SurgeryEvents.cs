using Content.Shared.Body.Organs;
using Content.Shared.FixedPoint;

namespace Content.Shared.Medical.Surgery;

/// <summary>
///     Raised to check if a surgery step can be performed on a target.
/// </summary>
[ByRefEvent]
public record struct SurgeryStepCheckEvent(EntityUid Tool, EntityUid Target, string Action, bool CanPerform, string? FailReason);

/// <summary>
///     Raised when a surgery step is performed.
/// </summary>
[ByRefEvent]
public record struct SurgeryStepPerformedEvent(EntityUid Tool, EntityUid Target, EntityUid Performer, string Action, bool Success);

/// <summary>
///     Raised to get the current surgery state of a body part.
/// </summary>
[ByRefEvent]
public record struct GetSurgeryStateEvent(EntityUid Target, SurgeryStage Stage);
