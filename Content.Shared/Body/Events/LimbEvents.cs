using Content.Shared.Body.Components;
using Content.Shared.Body.Organs;
using Content.Shared.FixedPoint;

namespace Content.Shared.Body.Events;

/// <summary>
///     The type of limb dismemberment.
/// </summary>
public enum DropLimbType : byte
{
    Edge,  // Cut clean off (sharp weapon)
    Blunt, // Shattered off (explosion)
    Burn,  // Burned away (extreme heat)
}

/// <summary>
///     Raised when a limb is fractured.
/// </summary>
[ByRefEvent]
public readonly record struct LimbFracturedEvent(Entity<ExternalOrganComponent> Limb);

/// <summary>
///     Raised when a limb is dismembered.
/// </summary>
[ByRefEvent]
public readonly record struct LimbDismemberedEvent(EntityUid Body, Entity<ExternalOrganComponent> Limb, DropLimbType Type);

/// <summary>
///     Raised to check if a limb should be fractured due to brute damage.
///     Handled by the server-side ExternalOrganSystem.
/// </summary>
[ByRefEvent]
public readonly record struct LimbFractureCheckEvent(Entity<ExternalOrganComponent> Limb, FixedPoint2 Damage);

/// <summary>
///     Raised to amputate a limb. Handled by the server-side ExternalOrganSystem.
/// </summary>
[ByRefEvent]
public readonly record struct LimbAmputateEvent(Entity<ExternalOrganComponent> Limb, DropLimbType Type);

/// <summary>
///     Raised when an entity's brain has died (integrity reached 0).
/// </summary>
[ByRefEvent]
public readonly record struct BrainDeathEvent(EntityUid Body, Entity<BrainComponent> Brain);
