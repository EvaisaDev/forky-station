using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organs;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.Damage.Systems;

/// <summary>
/// Distributes damage to specific limbs (external organs) based on target zone.
/// Also handles backward-compatible distribution when no target zone is specified.
/// </summary>
public sealed partial class LimbDamageSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DamageableSystem _damageable = default!;

    private readonly HashSet<ProtoId<DamageTypePrototype>> _bruteTypes = new()
    {
        "Blunt", "Slash", "Piercing"
    };

    private readonly HashSet<ProtoId<DamageTypePrototype>> _burnTypes = new()
    {
        "Heat", "Cold", "Shock", "Caustic"
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamageableComponent, DamageChangedEvent>(OnDamageChanged);
    }

    /// <summary>
    /// Apply damage to a specific limb (external organ).
    /// Also applies to the global DamageableComponent for backward compatibility.
    /// </summary>
    public void ApplyDamageToLimb(
        EntityUid body,
        EntityUid limbEntity,
        DamageSpecifier damage,
        bool ignoreResistances = false,
        EntityUid? origin = null)
    {
        // Get brute/burn split for this limb
        var (brute, burn) = SplitDamage(damage);

        // Apply to limb's external organ component
        if (TryComp<ExternalOrganComponent>(limbEntity, out var externalOrgan))
        {
            ApplyToLimbComponent((limbEntity, externalOrgan), brute, burn);
        }

        // Apply to global damage pool for backward compatibility
        _damageable.TryChangeDamage(body, damage, ignoreResistances, origin: origin);
    }

    /// <summary>
    /// Gets all external organs (limbs) from a body.
    /// </summary>
    public List<Entity<ExternalOrganComponent>> GetLimbs(EntityUid body)
    {
        var limbs = new List<Entity<ExternalOrganComponent>>();

        if (!TryComp<BodyComponent>(body, out var bodyComp) || bodyComp.Organs == null)
            return limbs;

        foreach (var organ in bodyComp.Organs.ContainedEntities)
        {
            if (TryComp<ExternalOrganComponent>(organ, out var extOrgan))
            {
                limbs.Add((organ, extOrgan));
            }
        }

        return limbs;
    }

    /// <summary>
    /// Splits a DamageSpecifier into brute (Blunt+Slash+Piercing) and burn (Heat+Cold+Shock+Caustic) totals.
    /// </summary>
    public (FixedPoint2 Brute, FixedPoint2 Burn) SplitDamage(DamageSpecifier damage)
    {
        FixedPoint2 brute = FixedPoint2.Zero;
        FixedPoint2 burn = FixedPoint2.Zero;

        foreach (var (type, value) in damage.DamageDict)
        {
            if (value <= 0)
                continue;

            if (_bruteTypes.Contains(type))
                brute += value;
            else if (_burnTypes.Contains(type))
                burn += value;
        }

        return (brute, burn);
    }

    /// <summary>
    /// When global damage changes without a specific target zone,
    /// distribute the damage evenly across all limbs.
    /// This is a fallback for systems that don't specify a limb target.
    /// </summary>
    private void OnDamageChanged(Entity<DamageableComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageDelta == null || !args.DamageIncreased)
            return;

        // Only distribute if this entity has a body with external organs
        if (!TryComp<BodyComponent>(ent, out var body) || body.Organs == null)
            return;

        var limbs = GetLimbs(ent);
        if (limbs.Count == 0)
            return;

        var (totalBrute, totalBurn) = SplitDamage(args.DamageDelta);

        if (totalBrute <= 0 && totalBurn <= 0)
            return;

        // Distribute evenly across limbs
        var brutePerLimb = totalBrute / limbs.Count;
        var burnPerLimb = totalBurn / limbs.Count;
        var bruteRemainder = totalBrute - brutePerLimb * limbs.Count;
        var burnRemainder = totalBurn - burnPerLimb * limbs.Count;

        for (var i = 0; i < limbs.Count; i++)
        {
            var limb = limbs[i];
            var addBrute = brutePerLimb;
            var addBurn = burnPerLimb;

            // Distribute remainder randomly
            if (bruteRemainder > 0 && _random.Prob(0.5f))
            {
                addBrute += FixedPoint2.New(1);
                bruteRemainder -= FixedPoint2.New(1);
            }
            if (burnRemainder > 0 && _random.Prob(0.5f))
            {
                addBurn += FixedPoint2.New(1);
                burnRemainder -= FixedPoint2.New(1);
            }

            ApplyToLimbComponent(limb, addBrute, addBurn);
        }
    }

    private void ApplyToLimbComponent(Entity<ExternalOrganComponent> limb, FixedPoint2 brute, FixedPoint2 burn)
    {
        limb.Comp.BruteDamage += brute;
        limb.Comp.BurnDamage += burn;

        // Clamp to max damage
        if (limb.Comp.BruteDamage + limb.Comp.BurnDamage > limb.Comp.MaxDamage)
        {
            var excess = (limb.Comp.BruteDamage + limb.Comp.BurnDamage) - limb.Comp.MaxDamage;
            // Reduce proportionally
            var totalDamage = limb.Comp.BruteDamage + limb.Comp.BurnDamage;
            if (totalDamage > 0)
            {
                var bruteFraction = limb.Comp.BruteDamage / totalDamage;
                limb.Comp.BruteDamage -= excess * bruteFraction;
                limb.Comp.BurnDamage -= excess * (1 - bruteFraction);
            }
        }

        Dirty(limb);

        // Check for fracture
        if ((limb.Comp.Flags & LimbFlags.CanBreak) != 0 &&
            limb.Comp.BruteDamage >= limb.Comp.MinBrokenDamage &&
            (limb.Comp.Status & OrganStatusFlags.Broken) == 0)
        {
            // Use the server-side system if available
            var ev = new LimbFractureCheckEvent(limb, brute);
            RaiseLocalEvent(limb, ref ev);
        }
    }
}


