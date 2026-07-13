using System.Linq;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organs;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Hitscan.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Network;

namespace Content.Shared.Damage.Systems;

public sealed partial class LimbDamageSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private INetManager _net = default!;

    private readonly HashSet<ProtoId<DamageTypePrototype>> _bruteTypes = new()
    {
        "Blunt", "Slash", "Piercing"
    };

    private readonly HashSet<ProtoId<DamageTypePrototype>> _burnTypes = new()
    {
        "Heat", "Cold", "Shock", "Caustic"
    };

    private bool _processingMelee;
    private bool _processingProjectile;
    private bool _processingHitscan;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit, before: new[] { typeof(DamageableSystem) });
        SubscribeLocalEvent<ProjectileComponent, ProjectileHitEvent>(OnProjectileHit, before: new[] { typeof(DamageableSystem) });
        SubscribeLocalEvent<HitscanBasicRaycastComponent, HitscanRaycastFiredEvent>(OnHitscanHit, before: new[] { typeof(HitscanBasicDamageSystem) });
        SubscribeLocalEvent<DamageableComponent, DamageChangedEvent>(OnDamageChanged);
    }

    /// <summary>
    ///     Melee attacks hit a single random limb.
    /// </summary>
    private void OnMeleeHit(Entity<MeleeWeaponComponent> weapon, ref MeleeHitEvent ev)
    {
        if (!_net.IsServer)
            return;

        try
        {
            _processingMelee = true;

            foreach (var target in ev.HitEntities)
            {
                if (!TryComp<BodyComponent>(target, out var body) || body.Organs == null)
                    continue;

                var limbs = GetLimbs(target);
                if (limbs.Count == 0)
                    continue;

                var (brute, burn) = SplitDamage(ev.BaseDamage);
                var limb = limbs[_random.Next(limbs.Count)];
                ApplyToLimbComponent(limb, brute, burn);

                _damageable.TryChangeDamage(target, ev.BaseDamage, origin: ev.User);
            }

            ev.Handled = true;
        }
        finally
        {
            _processingMelee = false;
        }
    }

    /// <summary>
    ///     Projectile hits should hit a single random limb (like melee).
    ///     Fires before TryChangeDamage, so we just set a flag.
    /// </summary>
    private void OnProjectileHit(Entity<ProjectileComponent> projectile, ref ProjectileHitEvent args)
    {
        if (!_net.IsServer)
            return;

        _processingProjectile = true;
    }

    /// <summary>
    ///     Hitscan (laser) hits should also hit a single random limb.
    ///     Fires before TryChangeDamage, so we set a flag.
    /// </summary>
    private void OnHitscanHit(Entity<HitscanBasicRaycastComponent> gun, ref HitscanRaycastFiredEvent args)
    {
        if (!_net.IsServer)
            return;

        if (args.Data.HitEntity == null)
            return;

        _processingHitscan = true;
    }

    /// <summary>
    ///     Untargeted damage (explosions, fire, environment) distributes across all limbs.
    ///     Melee, projectile, and hitscan damage go to a single random limb.
    /// </summary>
    private void OnDamageChanged(Entity<DamageableComponent> ent, ref DamageChangedEvent args)
    {
        if (!_net.IsServer)
            return;

        if (args.DamageDelta == null || !args.DamageIncreased)
            return;

        // Skip if damage was already handled by melee limb targeting
        if (_processingMelee)
            return;

        if (!TryComp<BodyComponent>(ent, out var body) || body.Organs == null)
        {
            _processingProjectile = false;
            _processingHitscan = false;
            return;
        }

        var limbs = GetLimbs(ent);
        if (limbs.Count == 0)
        {
            _processingProjectile = false;
            _processingHitscan = false;
            return;
        }

        var (totalBrute, totalBurn) = SplitDamage(args.DamageDelta);

        if (totalBrute <= 0 && totalBurn <= 0)
        {
            _processingProjectile = false;
            _processingHitscan = false;
            return;
        }

        // Projectile or hitscan: single random limb
        if (_processingProjectile || _processingHitscan)
        {
            _processingProjectile = false;
            _processingHitscan = false;
            if (totalBrute > 0)
            {
                var bruteLimb = limbs[_random.Next(limbs.Count)];
                ApplyToLimbComponent(bruteLimb, totalBrute, FixedPoint2.Zero);
            }
            if (totalBurn > 0)
            {
                var burnLimb = limbs[_random.Next(limbs.Count)];
                ApplyToLimbComponent(burnLimb, FixedPoint2.Zero, totalBurn);
            }
            return;
        }

        // Untargeted damage: distribute across all limbs
        var brutePerLimb = totalBrute / limbs.Count;
        var burnPerLimb = totalBurn / limbs.Count;
        var bruteRemainder = totalBrute - brutePerLimb * limbs.Count;
        var burnRemainder = totalBurn - burnPerLimb * limbs.Count;

        for (var i = 0; i < limbs.Count; i++)
        {
            var limb = limbs[i];
            var addBrute = brutePerLimb;
            var addBurn = burnPerLimb;

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

    public void ApplyDamageToLimb(
        EntityUid body,
        EntityUid limbEntity,
        DamageSpecifier damage,
        bool ignoreResistances = false,
        EntityUid? origin = null)
    {
        var (brute, burn) = SplitDamage(damage);

        if (TryComp<ExternalOrganComponent>(limbEntity, out var externalOrgan))
            ApplyToLimbComponent((limbEntity, externalOrgan), brute, burn);

        _damageable.TryChangeDamage(body, damage, ignoreResistances, origin: origin);
    }

    public List<Entity<ExternalOrganComponent>> GetLimbs(EntityUid body)
    {
        var limbs = new List<Entity<ExternalOrganComponent>>();

        if (!TryComp<BodyComponent>(body, out var bodyComp) || bodyComp.Organs == null)
            return limbs;

        foreach (var organ in bodyComp.Organs.ContainedEntities)
        {
            if (TryComp<ExternalOrganComponent>(organ, out var extOrgan))
                limbs.Add((organ, extOrgan));
        }

        return limbs;
    }

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

    private void ApplyToLimbComponent(Entity<ExternalOrganComponent> limb, FixedPoint2 brute, FixedPoint2 burn)
    {
        limb.Comp.BruteDamage += brute;
        limb.Comp.BurnDamage += burn;

        if (limb.Comp.BruteDamage + limb.Comp.BurnDamage > limb.Comp.MaxDamage)
        {
            var excess = (limb.Comp.BruteDamage + limb.Comp.BurnDamage) - limb.Comp.MaxDamage;
            var totalDamage = limb.Comp.BruteDamage + limb.Comp.BurnDamage;
            if (totalDamage > 0)
            {
                var bruteFraction = limb.Comp.BruteDamage / totalDamage;
                limb.Comp.BruteDamage -= excess * bruteFraction;
                limb.Comp.BurnDamage -= excess * (1 - bruteFraction);
            }
        }

        Dirty(limb);

        if ((limb.Comp.Flags & LimbFlags.CanBreak) != 0 &&
            limb.Comp.BruteDamage >= limb.Comp.MinBrokenDamage &&
            (limb.Comp.Status & OrganStatusFlags.Broken) == 0)
        {
            var ev = new LimbFractureCheckEvent(limb, brute);
            RaiseLocalEvent(limb, ref ev);
        }
    }
}
