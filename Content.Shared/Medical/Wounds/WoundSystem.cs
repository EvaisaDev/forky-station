using System.Linq;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organs;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Pain;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Network;

namespace Content.Shared.Medical.Wounds;

public sealed partial class WoundSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly ProtoId<DamageTypePrototype>[] BruteTypes = { "Blunt", "Slash", "Piercing" };
    private static readonly ProtoId<DamageTypePrototype>[] BurnTypes = { "Heat", "Cold", "Shock", "Caustic" };

    private TimeSpan _nextUpdate;
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyComponent, DamageChangedEvent>(OnBodyDamageChanged, after: new[] { typeof(LimbDamageSystem) });
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

        if (ent.Comp.Organs == null)
            return;

        // Check for weapon-specific wound type override
        EntProtoId? weaponWoundType = null;
        if (args.Origin is { } origin && TryComp<WoundTypeOverrideComponent>(origin, out var overrideComp))
            weaponWoundType = overrideComp.WoundType;

        var wBlunt = SumTypes(damage, new ProtoId<DamageTypePrototype>[] { "Blunt" });
        var wSlash = SumTypes(damage, new ProtoId<DamageTypePrototype>[] { "Slash" });
        var wPierce = SumTypes(damage, new ProtoId<DamageTypePrototype>[] { "Piercing" });
        var wBurn = SumTypes(damage, BurnTypes);

        foreach (var organ in ent.Comp.Organs.ContainedEntities)
        {
            if (!TryComp<WoundableComponent>(organ, out var woundable))
                continue;

            if (TryComp<ExternalOrganComponent>(organ, out var ext))
            {
                var needsBruteWound = (wBlunt > 0 || wSlash > 0 || wPierce > 0);
                var needsBurnWound = wBurn > 0;
                var hasLimbBrute = ext.BruteDamage > 0;
                var hasLimbBurn = ext.BurnDamage > 0;

                if ((needsBruteWound && !hasLimbBrute) && (needsBurnWound && !hasLimbBurn))
                    continue;

                if (needsBruteWound && !hasLimbBrute)
                    continue;
                if (needsBurnWound && !hasLimbBurn)
                    continue;
            }

            ProcessDamageForLimb((organ, woundable), ent.Owner, damage, weaponWoundType);
        }
    }

    private void ProcessDamageForLimb(Entity<WoundableComponent> limb, EntityUid bodyUid, DamageSpecifier damage,
        EntProtoId? weaponWoundType = null)
    {
        var blunt = SumTypes(damage, new ProtoId<DamageTypePrototype>[] { "Blunt" });
        var slash = SumTypes(damage, new ProtoId<DamageTypePrototype>[] { "Slash" });
        var pierce = SumTypes(damage, new ProtoId<DamageTypePrototype>[] { "Piercing" });
        var burn = SumTypes(damage, BurnTypes);

        var brute = blunt + slash + pierce;

        // Brittle limb: 1.5x blunt damage
        TryComp<ExternalOrganComponent>(limb, out var ext);
        if (ext != null && (ext.Status & OrganStatusFlags.Brittle) != 0 && blunt > 0)
        {
            var extraBlunt = FixedPoint2.Min(blunt * 0.5f, FixedPoint2.New(100));
            damage.DamageDict.TryGetValue("Blunt", out var existingBlunt);
            damage.DamageDict["Blunt"] = existingBlunt + extraBlunt;
            blunt += extraBlunt;
            brute += extraBlunt;
        }

        if (weaponWoundType != null)
        {
            AddWeaponSpecificWound(limb, bodyUid, damage, weaponWoundType.Value);
        }
        else
        {
            if (blunt > 0)
                AddDamageToWoundGroup(limb, bodyUid, damage, blunt, "Brute");
            if (slash > 0)
                AddDamageToWoundGroup(limb, bodyUid, damage, slash, "Cut");
            if (pierce > 0)
                AddDamageToWoundGroup(limb, bodyUid, damage, pierce, "Puncture");
            if (burn > 0)
                AddDamageToWoundGroup(limb, bodyUid, damage, burn, "Burn");
        }

        // Damage triggers for limb conditions
        if (ext != null && _net.IsServer)
        {
            var totalBrute = brute.Float();
            var totalBurn = burn.Float();

            // Artery cut: slash/pierce > 15 has a chance to sever artery
            if ((ext.Flags & LimbFlags.HasTendon) != 0
                && (ext.Status & OrganStatusFlags.ArteryCut) == 0
                && (ext.Status & OrganStatusFlags.Robotic) == 0)
            {
                var sharpDamage = slash.Float() + pierce.Float();
                if (sharpDamage > 15 && _random.Prob(Math.Min(sharpDamage, 75f) / 100f))
                {
                    ext.Status |= OrganStatusFlags.ArteryCut;
                    Dirty(limb, ext);
                    var msg = Loc.GetString("limb-artery-cut", ("limb", Name(limb)));
                    _popup.PopupEntity(msg, bodyUid, bodyUid);
                }
            }

            // Tendon cut: slash/pierce damage has a chance to sever tendon
            if ((ext.Flags & LimbFlags.HasTendon) != 0
                && (ext.Status & OrganStatusFlags.TendonCut) == 0
                && (ext.Status & OrganStatusFlags.Robotic) == 0)
            {
                var sharpDamage = slash.Float() + pierce.Float();
                if (sharpDamage > 10 && _random.Prob(Math.Min(sharpDamage / 4f, 50f) / 100f))
                {
                    ext.Status |= OrganStatusFlags.TendonCut;
                    Dirty(limb, ext);
                    var msg = Loc.GetString("limb-tendon-cut", ("limb", Name(limb)));
                    _popup.PopupEntity(msg, bodyUid, bodyUid);
                }
            }

            // Dislocation: high brute damage chance
            if (!ext.Dislocated && (ext.Status & OrganStatusFlags.Robotic) == 0 && brute.Float() > 20
                && (ext.Flags & LimbFlags.CanBreak) != 0)
            {
                if (_random.Prob(Math.Min(brute.Float() / 100f, 0.5f)))
                {
                    ext.Dislocated = true;
                    Dirty(limb, ext);
                    var msg = Loc.GetString("limb-dislocated", ("limb", Name(limb)));
                    _popup.PopupEntity(msg, bodyUid, bodyUid);
                }
            }

            // Disfigurement: severe damage to head
            if (!ext.Disfigured && TryComp<OrganComponent>(limb, out var organComp)
                && organComp.Category == "Head")
            {
                if (totalBrute > 30 || totalBurn > 20)
                {
                    if (_random.Prob(Math.Min((totalBrute + totalBurn) / 100f, 0.75f)))
                    {
                        ext.Disfigured = true;
                        Dirty(limb, ext);
                        var msg = Loc.GetString("limb-disfigured");
                        _popup.PopupEntity(msg, bodyUid, bodyUid);
                    }
                }
            }

        }

        // Baystation-style pain spike: add_pain(0.6*burn + 0.4*brute)
        _pain.AddPainSpike(limb, brute.Float(), burn.Float());
    }

    private void AddWeaponSpecificWound(Entity<WoundableComponent> limb, EntityUid bodyUid,
        DamageSpecifier damage, EntProtoId woundProtoId)
    {
        var entCoords = Transform(limb.Owner).Coordinates;
        var woundEnt = Spawn(woundProtoId, entCoords);

        if (!TryComp<WoundComponent>(woundEnt, out var newWound))
        {
            Del(woundEnt);
            return;
        }

        newWound.ParentWoundable = limb.Owner;
        newWound.CreatedAt = _timing.CurTime;
        var totalDamage = damage.GetTotal();
        if (totalDamage > 0)
            TransferDamage(newWound, damage, totalDamage, totalDamage);
        Dirty(woundEnt, newWound);

        InitializeWoundEffects(woundEnt);

        limb.Comp.Wounds.Add(woundEnt);
        Dirty(limb.Owner, limb.Comp);

        MergeCompatibleWounds(limb);
    }

    private void InitializeWoundEffects(EntityUid woundEnt)
    {
        if (!TryComp<WoundEffectsComponent>(woundEnt, out var effects))
            return;

        foreach (var instance in effects.Effects)
        {
            var proto = _prototype.Index(instance.Id);
            switch (proto.EffectType)
            {
                case "Bleeding":
                    var baseAmount = instance.GetConfigFloat("baseBleedAmount", _prototype);
                    instance.SetFloat("currentBleedAmount", baseAmount);
                    var bleedTimer = instance.GetConfigFloat("bleedTimer", _prototype);
                    instance.SetFloat("bleedTimer", bleedTimer);
                    break;
            }
        }
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

                var tended = TryComp<WoundEffectsComponent>(woundUid, out var effects)
                    && effects.GetEffect("Tendable", _prototype) is { } tendEffect
                    && tendEffect.GetFloat("tended") > 0;
                if (tended)
                    AddToDict(args.Tended.DamageDict, type, value);
            }
        }
    }

    private void OnWoundGetBleed(Entity<WoundComponent> ent, ref GetBleedLevelEvent args)
    {
        if (!TryComp<WoundEffectsComponent>(ent, out var effects))
            return;

        var bleedInstance = effects.GetEffect("Bleeding", _prototype);
        if (bleedInstance == null)
            return;

        // Baystation: bruises only bleed at moderate+ severity (damage >= 20)
        if (ent.Comp.Group == "Brute" && ent.Comp.Damage.GetTotal() < 20)
            return;

        // Baystation: bleed timer — wound stops bleeding naturally when timer expires
        var bleedTimer = bleedInstance.GetFloatOrConfig("bleedTimer", _prototype);
        if (bleedTimer <= 0)
            return;

        var bleedAmount = bleedInstance.GetFloatOrConfig("currentBleedAmount", _prototype);
        if (bleedAmount <= 0)
            return;

        var clampInstance = effects.GetEffect("Clampable", _prototype);
        if (clampInstance != null && clampInstance.GetFloat("clamped") > 0)
        {
            args.BleedAmount += FixedPoint2.New(bleedAmount * 0.3f);
            return;
        }

        var tendInstance = effects.GetEffect("Tendable", _prototype);
        if (tendInstance != null && tendInstance.GetFloat("tended") > 0)
        {
            args.BleedAmount += FixedPoint2.New(bleedAmount * 0.1f);
            return;
        }

        args.BleedAmount += FixedPoint2.New(bleedAmount);
    }

    private void OnWoundGetPain(Entity<WoundComponent> ent, ref GetPainEvent args)
    {
        if (!TryComp<WoundEffectsComponent>(ent, out var effects))
            return;

        var painInstance = effects.GetEffect("Pain", _prototype);
        if (painInstance == null)
            return;

        var painAmount = painInstance.GetFloatOrConfig("painAmount", _prototype);
        var freshPain = painInstance.GetFloatOrConfig("freshPainAmount", _prototype);

        args.PainAmount += FixedPoint2.New(painAmount);
        args.FreshPainAmount += FixedPoint2.New(freshPain);
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

    private void AddDamageToWoundGroup(Entity<WoundableComponent> limb, EntityUid bodyUid,
        DamageSpecifier damage, FixedPoint2 groupTotal, string groupName)
    {
        // Baystation: first try to worsen existing wounds (widen them) before filling space
        if (TryWorsenExistingWounds(limb, damage, ref groupTotal, groupName))
            return;

        // Fill remaining capacity in existing wounds
        foreach (var woundUid in limb.Comp.Wounds)
        {
            if (TerminatingOrDeleted(woundUid))
                continue;
            if (!TryComp<WoundComponent>(woundUid, out var wound))
                continue;
            if (!IsCorrectWoundGroup(woundUid, groupName))
                continue;

            var space = wound.MaximumDamage - wound.Damage.GetTotal();
            if (space <= 0)
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

        // Check capacity before spawning a new wound
        if (IsCapacityReached(limb, bodyUid, groupName))
        {
            ReplaceOldestWound(limb, bodyUid, damage, groupTotal, groupName);
            return;
        }

        SpawnNewWound(limb, damage, groupTotal, groupName);
    }

    /// <summary>
    /// Baystation: try to worsen an existing compatible wound instead of creating a new one.
    /// A wound can be worsened if it's the same damage type and the combined damage
    /// doesn't exceed 1.5x the first stage's damage threshold.
    /// Merged wounds (amount > 1) cannot be worsened.
    /// </summary>
    private bool TryWorsenExistingWounds(Entity<WoundableComponent> limb, DamageSpecifier damage,
        ref FixedPoint2 groupTotal, string groupName)
    {
        foreach (var woundUid in limb.Comp.Wounds)
        {
            if (TerminatingOrDeleted(woundUid))
                continue;
            if (!TryComp<WoundComponent>(woundUid, out var wound))
                continue;
            if (!IsCorrectWoundGroup(woundUid, groupName))
                continue;

            // Baystation: merged wounds (multiple wounds of same type stacked) can't be worsened
            if (wound.Damage.GetTotal() > wound.MaximumDamage * 0.8f)
                continue;

            // Check if the wound can absorb more damage (1.5x first stage threshold)
            // For our system, use 1.5x maximumDamage as the cap
            var maxWorsenDmg = wound.MaximumDamage.Float() * 1.5f;
            var currentDmg = wound.Damage.GetTotal().Float();
            if (currentDmg + groupTotal.Float() > maxWorsenDmg)
                continue;

            // Worsen: add the damage directly to this wound
            TransferDamage(wound, damage, groupTotal, groupTotal);
            Dirty(woundUid, wound);
            var refresh = new RefreshWoundsEvent();
            RaiseLocalEvent(woundUid, ref refresh);
            groupTotal = FixedPoint2.Zero;
            MergeCompatibleWounds(limb);
            return true;
        }

        return false;
    }

    private bool IsCapacityReached(Entity<WoundableComponent> limb, EntityUid bodyUid, string groupName)
    {
        if (!TryComp<WoundCapacityComponent>(bodyUid, out var capacity))
            return false;

        var maxPerGroup = capacity.GroupCapacity.GetValueOrDefault(groupName, capacity.DefaultCapacity);
        if (maxPerGroup <= 0)
            return true;

        var count = 0;
        foreach (var woundUid in limb.Comp.Wounds)
        {
            if (TerminatingOrDeleted(woundUid))
                continue;
            if (!TryComp<WoundComponent>(woundUid, out var wound))
                continue;
            if (string.IsNullOrEmpty(wound.Group) || !wound.Group.Equals(groupName, StringComparison.OrdinalIgnoreCase))
                continue;
            count++;
        }

        return count >= maxPerGroup;
    }

    private void ReplaceOldestWound(Entity<WoundableComponent> limb, EntityUid bodyUid,
        DamageSpecifier damage, FixedPoint2 groupTotal, string groupName)
    {
        EntityUid? oldestWound = null;
        TimeSpan oldestTime = TimeSpan.MaxValue;

        foreach (var woundUid in limb.Comp.Wounds)
        {
            if (TerminatingOrDeleted(woundUid))
                continue;
            if (!TryComp<WoundComponent>(woundUid, out var wound))
                continue;
            if (string.IsNullOrEmpty(wound.Group) || !wound.Group.Equals(groupName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (wound.CreatedAt < oldestTime)
            {
                oldestTime = wound.CreatedAt;
                oldestWound = woundUid;
            }
        }

        if (oldestWound is not { } remove)
        {
            SpawnNewWound(limb, damage, groupTotal, groupName);
            return;
        }

        if (TryComp<WoundComponent>(remove, out var removeWound))
        {
            foreach (var (type, value) in removeWound.Damage.DamageDict)
            {
                if (value > 0)
                {
                    if (damage.DamageDict.TryGetValue(type, out var existing))
                        damage.DamageDict[type] = existing + value;
                    else
                        damage.DamageDict[type] = value;
                }
            }
            groupTotal += removeWound.Damage.GetTotal();
        }

        limb.Comp.Wounds.Remove(remove);
        Del(remove);

        SpawnNewWound(limb, damage, groupTotal, groupName);
    }

    private void SpawnNewWound(Entity<WoundableComponent> limb, DamageSpecifier damage,
        FixedPoint2 groupTotal, string groupName)
    {
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

        InitializeWoundEffects(woundEnt);

        limb.Comp.Wounds.Add(woundEnt);
        Dirty(limb.Owner, limb.Comp);

        MergeCompatibleWounds(limb);
    }

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

                if (!IsSameGroup(a, b))
                    continue;

                // Check same treatment state via WoundEffectsComponent
                if (!IsSameTreatmentState(a, b))
                    continue;

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

                var combined = wk.Damage.GetTotal() + wr.Damage.GetTotal();
                if (combined > wk.MaximumDamage)
                {
                    var groupName = GetWoundGroup(keep);
                    if (string.IsNullOrEmpty(groupName))
                        continue;

                    var newProto = PickWoundPrototype(groupName, combined);
                    if (newProto == null)
                        continue;

                    var entCoords = Transform(limb.Owner).Coordinates;
                    var newWoundEnt = Spawn(newProto, entCoords);
                    if (!TryComp<WoundComponent>(newWoundEnt, out var newWc))
                    {
                        Del(newWoundEnt);
                        continue;
                    }

                    newWc.ParentWoundable = limb.Owner;
                    newWc.CreatedAt = _timing.CurTime;
                    foreach (var (type, value) in wk.Damage.DamageDict)
                    {
                        if (value > 0)
                            newWc.Damage.DamageDict[type] = value;
                    }
                    foreach (var (type, value) in wr.Damage.DamageDict)
                    {
                        if (value <= 0)
                            continue;
                        if (newWc.Damage.DamageDict.TryGetValue(type, out var existing))
                            newWc.Damage.DamageDict[type] = existing + value;
                        else
                            newWc.Damage.DamageDict[type] = value;
                    }
                    Dirty(newWoundEnt, newWc);

                    InitializeWoundEffects(newWoundEnt);

                    limb.Comp.Wounds.Remove(keep);
                    limb.Comp.Wounds.Remove(remove);
                    limb.Comp.Wounds.Add(newWoundEnt);
                    Dirty(limb.Owner, limb.Comp);
                    Del(keep);
                    Del(remove);

                    return;
                }

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

                limb.Comp.Wounds.Remove(remove);
                Dirty(limb.Owner, limb.Comp);
                Del(remove);
            }
        }
    }

    private bool IsSameTreatmentState(EntityUid a, EntityUid b)
    {
        var aTended = TryComp<WoundEffectsComponent>(a, out var ea)
            && ea.GetEffect("Tendable", _prototype) is { } ta
            && ta.GetFloat("tended") > 0;
        var bTended = TryComp<WoundEffectsComponent>(b, out var eb)
            && eb.GetEffect("Tendable", _prototype) is { } tb
            && tb.GetFloat("tended") > 0;
        return aTended == bTended;
    }

    private bool IsSameGroup(EntityUid a, EntityUid b)
    {
        return GetWoundGroup(a) == GetWoundGroup(b);
    }

    private string GetWoundGroup(EntityUid wound)
    {
        if (TryComp<WoundComponent>(wound, out var wc) && !string.IsNullOrEmpty(wc.Group))
            return wc.Group;

        if (TryComp<WoundDescriptionComponent>(wound, out var desc))
        {
            foreach (var text in desc.Descriptions.Values)
            {
                if (text.Contains("brute", StringComparison.OrdinalIgnoreCase)) return "brute";
                if (text.Contains("cut", StringComparison.OrdinalIgnoreCase)) return "cut";
                if (text.Contains("puncture", StringComparison.OrdinalIgnoreCase)) return "puncture";
                if (text.Contains("burn", StringComparison.OrdinalIgnoreCase)) return "burn";
                if (text.Contains("incision", StringComparison.OrdinalIgnoreCase)) return "incision";
            }
        }
        return "";
    }

    private bool IsCorrectWoundGroup(EntityUid woundUid, string groupName)
    {
        if (TryComp<WoundComponent>(woundUid, out var wc) &&
            !string.IsNullOrEmpty(wc.Group) &&
            wc.Group.Equals(groupName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (TryComp<WoundDescriptionComponent>(woundUid, out var desc))
        {
            foreach (var text in desc.Descriptions.Values)
            {
                if (text.Contains(groupName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
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
