using System;
using System.Linq;
using System.Linq;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organs;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
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
    [Dependency] private IRobustRandom _random = default!;

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

        // Distribute wounds only to limbs that actually received this damage.
        // For melee: LimbDamageSystem applies all damage to one random limb before
        // calling TryChangeDamage, so only that limb has matching ExternalOrganComponent damage.
        // For untargeted damage: OnDamageChanged distributes across all limbs first,
        // so all woundable limbs will have matching damage.
        if (ent.Comp.Organs == null)
            return;

        var wBlunt = SumTypes(damage, new ProtoId<DamageTypePrototype>[] { "Blunt" });
        var wSlash = SumTypes(damage, new ProtoId<DamageTypePrototype>[] { "Slash" });
        var wPierce = SumTypes(damage, new ProtoId<DamageTypePrototype>[] { "Piercing" });
        var wBurn = SumTypes(damage, BurnTypes);

        foreach (var organ in ent.Comp.Organs.ContainedEntities)
        {
            if (!TryComp<WoundableComponent>(organ, out var woundable))
                continue;

            // Only create wounds on limbs that actually received this damage.
            // Check if this limb has matching ExternalOrganComponent damage values.
            if (TryComp<ExternalOrganComponent>(organ, out var ext))
            {
                var needsBruteWound = (wBlunt > 0 || wSlash > 0 || wPierce > 0);
                var needsBurnWound = wBurn > 0;
                var hasLimbBrute = ext.BruteDamage > 0;
                var hasLimbBurn = ext.BurnDamage > 0;

                // Skip if the limb doesn't have the type of damage being applied
                if ((needsBruteWound && !hasLimbBrute) && (needsBurnWound && !hasLimbBurn))
                    continue;

                // For melee: only one limb has damage, so only that limb gets wounds
                // For explosions: all limbs have damage, so all get wounds
                if (needsBruteWound && !hasLimbBrute)
                    continue;
                if (needsBurnWound && !hasLimbBurn)
                    continue;
            }

            ProcessDamageForLimb((organ, woundable), damage);
        }
    }

    private void ProcessDamageForLimb(Entity<WoundableComponent> limb, DamageSpecifier damage)
    {
        var blunt = SumTypes(damage, new ProtoId<DamageTypePrototype>[] { "Blunt" });
        var slash = SumTypes(damage, new ProtoId<DamageTypePrototype>[] { "Slash" });
        var pierce = SumTypes(damage, new ProtoId<DamageTypePrototype>[] { "Piercing" });
        var burn = SumTypes(damage, BurnTypes);

        if (blunt > 0)
            AddDamageToWoundGroup(limb, damage, blunt, "Brute");
        if (slash > 0)
            AddDamageToWoundGroup(limb, damage, slash, "Cut");
        if (pierce > 0)
            AddDamageToWoundGroup(limb, damage, pierce, "Puncture");
        if (burn > 0)
            AddDamageToWoundGroup(limb, damage, burn, "Burn");
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
            MergeCompatibleWounds(limb);
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

        // Try merging compatible wounds after adding damage
        MergeCompatibleWounds(limb);
    }

    /// <summary>
    ///     Merges compatible wounds on the same limb.
    ///     Same type + same treatment state + combined damage ≤ MaxDamage of the larger wound.
    /// </summary>
    private void MergeCompatibleWounds(Entity<WoundableComponent> limb)
    {
        var wounds = limb.Comp.Wounds.ToList();
        for (var i = 0; i < wounds.Count; i++)
        {
            var a = wounds[i];
            if (TerminatingOrDeleted(a) || !TryComp<WoundComponent>(a, out var wa))
                continue;

            for (var j = i + 1; j < wounds.Count; j++)
            {
                var b = wounds[j];
                if (TerminatingOrDeleted(b) || !TryComp<WoundComponent>(b, out var wb))
                    continue;

                // Check same wound group
                if (!IsSameGroup(a, b))
                    continue;

                // Check same treatment state
                var aTended = TryComp<TendableWoundComponent>(a, out var ta) && ta.Tended;
                var bTended = TryComp<TendableWoundComponent>(b, out var tb) && tb.Tended;
                if (aTended != bTended)
                    continue;

                // Merge: absorb the smaller wound into the larger one
                EntityUid keep, remove;
                WoundComponent wk, wr;

                if (wa.MaximumDamage >= wb.MaximumDamage)
                {
                    keep = a; wk = wa; remove = b; wr = wb;
                }
                else
                {
                    keep = b; wk = wb; remove = a; wr = wa;
                }

                // Check combined doesn't exceed max
                var combined = wk.Damage.GetTotal() + wr.Damage.GetTotal();
                if (combined > wk.MaximumDamage)
                    continue;

                // Transfer damage
                foreach (var (type, value) in wr.Damage.DamageDict)
                {
                    if (value <= 0)
                        continue;
                    if (wk.Damage.DamageDict.TryGetValue(type, out var existing))
                        wk.Damage.DamageDict[type] = existing + value;
                    else
                        wk.Damage.DamageDict[type] = value;
                }

                Dirty(keep, wk);

                // Remove the absorbed wound
                limb.Comp.Wounds.Remove(remove);
                Dirty(limb.Owner, limb.Comp);
                Del(remove);
            }
        }
    }

    private bool IsSameGroup(EntityUid a, EntityUid b)
    {
        return GetWoundGroup(a) == GetWoundGroup(b);
    }

    private string GetWoundGroup(EntityUid wound)
    {
        // Determine group from the WoundDescription prototype ID
        if (!TryComp<WoundDescriptionComponent>(wound, out var desc))
            return "";

        foreach (var text in desc.Descriptions.Values)
        {
            // description IDs are like "wound-brute-small", "wound-cut-moderate"
            if (text.Contains("brute", StringComparison.OrdinalIgnoreCase)) return "brute";
            if (text.Contains("cut", StringComparison.OrdinalIgnoreCase)) return "cut";
            if (text.Contains("puncture", StringComparison.OrdinalIgnoreCase)) return "puncture";
            if (text.Contains("burn", StringComparison.OrdinalIgnoreCase)) return "burn";
            if (text.Contains("incision", StringComparison.OrdinalIgnoreCase)) return "incision";
        }
        return "";
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
        var suffix = (groupName, severity) switch
        {
            ("Brute", >= 80) => "Monumental",
            ("Brute", >= 50) => "Huge",
            ("Brute", >= 30) => "Large",
            ("Brute", >= 20) => "Moderate",
            ("Brute", >= 10) => "Small",
            ("Brute", _) => "Tiny",

            ("Burn", >= 50) => "Carbonised",
            ("Burn", >= 40) => "Deep",
            ("Burn", >= 30) => "Severe",
            ("Burn", >= 15) => "Large",
            ("Burn", >= 10) => "Moderate",
            ("Burn", _) => "Small",

            ("Cut", >= 50) => "Massive",
            ("Cut", >= 25) => "Gaping",
            ("Cut", >= 15) => "Flesh",
            ("Cut", >= 10) => "Deep",
            ("Cut", _) => "Small",

            ("Puncture", >= 30) => "Massive",
            ("Puncture", >= 15) => "Gaping",
            ("Puncture", >= 10) => "Flesh",
            ("Puncture", _) => "Small",

            _ => null
        };

        return suffix != null ? $"Wound{groupName}{suffix}" : null;
    }
}
