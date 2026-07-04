using System;
using System.Linq;
using Content.Shared.Body;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Network;

namespace Content.Shared.Medical.Wounds;

/// <summary>
///     Entity-based wound system. Listens for damage on body entities,
///     distributes wounds to all woundable limbs.
/// </summary>
public sealed partial class WoundSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;

    private static readonly ProtoId<DamageTypePrototype>[] BruteTypes = { "Blunt", "Slash", "Piercing" };
    private static readonly ProtoId<DamageTypePrototype>[] BurnTypes = { "Heat", "Cold", "Shock", "Caustic" };

    private TimeSpan _nextUpdate;
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyComponent, DamageChangedEvent>(OnBodyDamageChanged);
        SubscribeLocalEvent<WoundableComponent, GetWoundDamageEvent>(OnGetWoundDamage);
        SubscribeLocalEvent<WoundComponent, GetBleedLevelEvent>(OnWoundGetBleed);
        SubscribeLocalEvent<WoundComponent, GetPainEvent>(OnWoundGetPain);
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        base.Update(frameTime);

        var curTime = _timing.CurTime;
        if (curTime < _nextUpdate)
            return;

        _nextUpdate = curTime + UpdateInterval;

        var query = EntityQueryEnumerator<WoundableComponent>();
        while (query.MoveNext(out var uid, out var woundable))
        {
            // Clean up stale wound references
            if (woundable.Wounds.RemoveAll(w => !Exists(w) || TerminatingOrDeleted(w)) > 0)
                Dirty(uid, woundable);

            var ev = new GetWoundDamageEvent(new(), new());
            RaiseLocalEvent(uid, ref ev);
            woundable.TotalDamage = ev.Accumulator;
            woundable.TendedDamage = ev.Tended ?? new();
            Dirty(uid, woundable);
        }
    }

    private void OnBodyDamageChanged(Entity<BodyComponent> ent, ref DamageChangedEvent args)
    {
        if (!_net.IsServer)
            return;

        if (args.DamageDelta == null || !args.DamageIncreased)
            return;

        if (_timing.ApplyingState)
            return;

        var damage = DamageSpecifier.GetPositive(args.DamageDelta);
        if (damage.Empty)
            return;

        // Distribute wounds to all woundable limbs on this body
        if (ent.Comp.Organs == null)
            return;

        foreach (var organ in ent.Comp.Organs.ContainedEntities)
        {
            if (!TryComp<WoundableComponent>(organ, out var woundable))
                continue;

            ProcessDamageForLimb((organ, woundable), damage);
        }
    }

    private void ProcessDamageForLimb(Entity<WoundableComponent> limb, DamageSpecifier damage)
    {
        var bruteTotal = SumTypes(damage, BruteTypes);
        var burnTotal = SumTypes(damage, BurnTypes);

        if (bruteTotal > 0)
            AddDamageToWoundGroup(limb, damage, bruteTotal, "Brute");

        if (burnTotal > 0)
            AddDamageToWoundGroup(limb, damage, burnTotal, "Burn");
    }

    private void OnGetWoundDamage(Entity<WoundableComponent> ent, ref GetWoundDamageEvent args)
    {
        foreach (var woundUid in ent.Comp.Wounds)
        {
            if (!TryComp<WoundComponent>(woundUid, out var wound) || TerminatingOrDeleted(woundUid))
                continue;

            foreach (var (type, value) in wound.Damage.DamageDict)
            {
                if (value == 0)
                    continue;

                AddToDict(args.Accumulator.DamageDict, type, value);

                if (args.Tended == null)
                    continue;

                var tended = TryComp<TendableWoundComponent>(woundUid, out var t) && t.Tended;
                if (tended)
                    AddToDict(args.Tended.DamageDict, type, value);
            }
        }
    }

    private void OnWoundGetBleed(Entity<WoundComponent> ent, ref GetBleedLevelEvent args)
    {
        if (!TryComp<BleedingWoundComponent>(ent, out var bleeding) || bleeding.CurrentBleedAmount <= 0)
            return;

        if (TryComp<ClampableWoundComponent>(ent, out var clampable) && clampable.Clamped)
        {
            args.BleedAmount += bleeding.CurrentBleedAmount * 0.3f;
            return;
        }

        if (TryComp<TendableWoundComponent>(ent, out var tendable) && tendable.Tended)
        {
            args.BleedAmount += bleeding.CurrentBleedAmount * 0.1f;
            return;
        }

        args.BleedAmount += bleeding.CurrentBleedAmount;
    }

    private void OnWoundGetPain(Entity<WoundComponent> ent, ref GetPainEvent args)
    {
        if (!TryComp<PainfulWoundComponent>(ent, out var painful))
            return;

        args.PainAmount += painful.PainAmount;
        args.FreshPainAmount += painful.FreshPainAmount;
    }

    private static FixedPoint2 SumTypes(DamageSpecifier damage, ProtoId<DamageTypePrototype>[] types)
    {
        FixedPoint2 total = FixedPoint2.Zero;
        foreach (var type in types)
        {
            if (damage.DamageDict.TryGetValue(type, out var value) && value > 0)
                total += value;
        }
        return total;
    }

    private static void AddToDict(Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2> dict, ProtoId<DamageTypePrototype> key, FixedPoint2 value)
    {
        if (dict.TryGetValue(key, out var existing))
            dict[key] = existing + value;
        else
            dict[key] = value;
    }

    private void AddDamageToWoundGroup(Entity<WoundableComponent> limb, DamageSpecifier damage, FixedPoint2 groupTotal, string groupName)
    {
        foreach (var woundUid in limb.Comp.Wounds)
        {
            if (!TryComp<WoundComponent>(woundUid, out var wound) || TerminatingOrDeleted(woundUid))
                continue;

            var space = wound.MaximumDamage - wound.Damage.GetTotal();
            if (space <= 0)
                continue;

            if (!IsCorrectWoundGroup(woundUid, groupName))
                continue;

            var toAdd = FixedPoint2.Min(groupTotal, space);
            if (toAdd <= 0)
                continue;

            TransferDamage(wound, damage, groupTotal, toAdd);
            groupTotal -= toAdd;
            Dirty(woundUid, wound);
            var refresh = new RefreshWoundsEvent();
            RaiseLocalEvent(woundUid, ref refresh);
        }

        if (groupTotal <= 0)
            return;

        var protoId = PickWoundPrototype(groupName, groupTotal);
        if (protoId == null)
            return;

        var entCoords = Transform(limb.Owner).Coordinates;
        var woundEnt = Spawn(protoId, entCoords);

        if (!TryComp<WoundComponent>(woundEnt, out var newWound))
        {
            Del(woundEnt);
            return;
        }

        newWound.ParentWoundable = limb.Owner;
        newWound.CreatedAt = _timing.CurTime;
        TransferDamage(newWound, damage, groupTotal, groupTotal);
        Dirty(woundEnt, newWound);

        limb.Comp.Wounds.Add(woundEnt);
        Dirty(limb.Owner, limb.Comp);
    }

    private bool IsCorrectWoundGroup(EntityUid woundUid, string groupName)
    {
        if (!TryComp<WoundDescriptionComponent>(woundUid, out var desc))
            return false;

        // Check by name first (fast path)
        if (TryComp<MetaDataComponent>(woundUid, out var meta))
        {
            var name = meta.EntityName;
            if (name.Contains(groupName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Fallback: check description text
        foreach (var text in desc.Descriptions.Values)
        {
            if (text.Contains(groupName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void TransferDamage(WoundComponent wound, DamageSpecifier source, FixedPoint2 sourceTotal, FixedPoint2 amount)
    {
        if (sourceTotal <= 0)
            return;

        var ratio = (float)(amount / sourceTotal).Float();
        foreach (var (type, value) in source.DamageDict)
        {
            if (value <= 0)
                continue;

            var toTransfer = FixedPoint2.New(value.Float() * ratio);

            if (wound.Damage.DamageDict.TryGetValue(type, out var existing))
                wound.Damage.DamageDict[type] = existing + toTransfer;
            else
                wound.Damage.DamageDict[type] = toTransfer;
        }
    }

    private static string? PickWoundPrototype(string groupName, FixedPoint2 amount)
    {
        var severity = amount.Float();
        var baseName = groupName switch
        {
            "Brute" => "WoundBrute",
            "Burn" => "WoundBurn",
            _ => null
        };

        if (baseName == null)
            return null;

        var suffix = severity switch
        {
            >= 25 => "Severe",
            >= 12 => "Moderate",
            _ => "Small"
        };

        return $"{baseName}{suffix}";
    }
}
