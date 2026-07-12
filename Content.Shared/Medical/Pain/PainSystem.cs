using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organs;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.Medical.Pain;

public sealed partial class PainSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;

    private const float PARACETAMOL_PER_UNIT = 2.5f;
    private const float TRAMADOL_PER_UNIT = 3.5f;
    private const float OXYCODONE_PER_UNIT = 5.5f;

    private const float PAIN_DECAY_PER_SECOND = 0.05f;
    private const float BROKEN_PAIN = 10f;
    private const float DISLOCATED_PAIN = 5f;
    private const float BRUTE_PAIN_MULT = 0.7f;
    private const float BURN_PAIN_MULT = 0.8f;

    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PainComponent, MapInitEvent>(OnMapInit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<PainComponent>();
        while (query.MoveNext(out var uid, out var pain))
        {
            if (curTime < pain.NextUpdate)
                continue;

            pain.NextUpdate = curTime + UpdateInterval;

            if (!TryComp<BodyComponent>(uid, out var body) || body.Organs == null)
                continue;

            var painkillerLevel = GetPainkillerLevel(uid);

            var (maxLimbPain, worstOrgan, worstWoundable) = ComputePerLimbPain(body, painkillerLevel);

            if (!_net.IsServer)
            {
                pain.PainkillerLevel = painkillerLevel;
                pain.ShockLevel = maxLimbPain;
                continue;
            }

            // Contextual pain message for the worst limb
            if (worstOrgan is { } organ && worstWoundable is { } woundable)
            {
                HandlePainMessage(uid, organ, woundable, maxLimbPain, painkillerLevel, pain, curTime);

                // Behavioral effects at high pain
                if (maxLimbPain > 50 && _random.Prob(0.2f))
                    TryDropItem(uid, organ);

                if (maxLimbPain > 80 && _random.Prob(0.3f))
                    TryStun(uid);
            }

            pain.PainkillerLevel = painkillerLevel;
            pain.ShockLevel = maxLimbPain;
        }
    }

    private void OnMapInit(Entity<PainComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.ShockLevel = 0;
        ent.Comp.PainkillerLevel = 0;
        ent.Comp.LastPainMessageTime = TimeSpan.Zero;
        ent.Comp.LastPainMessage = string.Empty;
        ent.Comp.NextUpdate = _timing.CurTime + UpdateInterval;
    }

    private (float totalPain, EntityUid? worstOrgan, WoundableComponent? worstWoundable) ComputePerLimbPain(
        BodyComponent body, float painkillerLevel)
    {
        float totalPain = 0;
        EntityUid? worstOrgan = null;
        WoundableComponent? worstWoundable = null;
        var maxPain = 0f;

        if (body.Organs == null)
            return (0, null, null);

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (!TryComp<ExternalOrganComponent>(organ, out var ext))
                continue;

            if (!TryComp<WoundableComponent>(organ, out var woundable))
                continue;

            // Decay persistent pain naturally
            if (woundable.Pain > 0)
            {
                woundable.Pain = Math.Max(0, woundable.Pain - PAIN_DECAY_PER_SECOND * (float)UpdateInterval.TotalSeconds);
                Dirty(organ, woundable);
            }

            var limbPain = woundable.Pain;

            // Pain from wounds' Pain effects
            foreach (var woundUid in woundable.Wounds)
            {
                if (TerminatingOrDeleted(woundUid))
                    continue;

                if (!TryComp<WoundEffectsComponent>(woundUid, out var effects))
                    continue;

                var painInstance = effects.GetEffect("Pain", _prototype);
                if (painInstance == null)
                    continue;

                var painAmount = painInstance.GetFloatOrConfig("painAmount", _prototype);
                var freshPain = painInstance.GetFloatOrConfig("freshPainAmount", _prototype);
                limbPain += painAmount + freshPain;
            }

            // Pain from brute/burn damage (Baystation: 0.7*brute + 0.8*burn)
            limbPain += (float)ext.BruteDamage.Float() * BRUTE_PAIN_MULT;
            limbPain += (float)ext.BurnDamage.Float() * BURN_PAIN_MULT;

            // Pain from broken bones
            if ((ext.Status & OrganStatusFlags.Broken) != 0)
                limbPain += BROKEN_PAIN;

            // Pain from dislocation
            if (ext.Dislocated)
                limbPain += DISLOCATED_PAIN;

            totalPain += limbPain;

            // Baystation: prefer the most-damaged organ but randomize 30% to add variety
            if (limbPain > maxPain && (maxPain == 0 || _random.Prob(0.7f)))
            {
                maxPain = limbPain;
                worstOrgan = organ;
                worstWoundable = woundable;
            }
        }

        return (totalPain, worstOrgan, worstWoundable);
    }

    private void HandlePainMessage(EntityUid uid, EntityUid organ, WoundableComponent woundable,
        float limbPain, float painkillerLevel, PainComponent pain, TimeSpan curTime)
    {
        var effectivePain = limbPain - painkillerLevel * 0.5f;
        if (effectivePain <= 0)
            return;

        // Anti-spam: same message within cooldown
        var cooldown = TimeSpan.FromSeconds(Math.Max(2, 10 - effectivePain * 0.1));
        if (curTime - pain.LastPainMessageTime < cooldown)
            return;

        var limbName = Name(organ);
        var burning = TryComp<ExternalOrganComponent>(organ, out var ext) && ext.BurnDamage > ext.BruteDamage;

        string msg;
        if (effectivePain >= 60)
            msg = Loc.GetString("pain-limb-extreme", ("limb", limbName), ("burning", burning ? "on fire" : "hurting terribly"));
        else if (effectivePain >= 20)
            msg = Loc.GetString("pain-limb-severe", ("limb", limbName), ("burning", burning ? "burns" : "hurts"));
        else
            msg = Loc.GetString("pain-limb-mild", ("limb", limbName), ("burning", burning ? "burns" : "hurts"));

        // Anti-spam: skip if same message recently shown
        if (msg == pain.LastPainMessage && curTime - pain.LastPainMessageTime < TimeSpan.FromSeconds(5))
            return;

        pain.LastPainMessage = msg;
        pain.LastPainMessageTime = curTime;

        _popup.PopupEntity(msg, uid, uid, PopupType.MediumCaution);

        // Scream emote at high pain
        if (effectivePain >= 40 && _random.Prob(Math.Min(effectivePain / 100f, 1f)))
        {
            var screamMsg = Loc.GetString("pain-scream-emote", ("target", uid));
            _popup.PopupEntity(screamMsg, uid, uid, PopupType.LargeCaution);
        }
    }

    private void TryDropItem(EntityUid uid, EntityUid organ)
    {
        if (!TryComp<ExternalOrganComponent>(organ, out var ext))
            return;

        if ((ext.Flags & LimbFlags.CanGrasp) == 0)
            return;

        // Find held items — simplified: trigger a drop via event or hands system
        // For now, just raise a generic pain event
        var ev = new PainDropItemEvent(uid);
        RaiseLocalEvent(uid, ref ev);
    }

    private void TryStun(EntityUid uid)
    {
        var ev = new PainStunEvent(uid);
        RaiseLocalEvent(uid, ref ev);
    }

    public void AddPainSpike(Entity<WoundableComponent> limb, float bruteAmount, float burnAmount)
    {
        var spikeAmount = bruteAmount * 0.4f + burnAmount * 0.6f;
        limb.Comp.Pain += spikeAmount;
        Dirty(limb.Owner, limb.Comp);
    }

    private float GetPainkillerLevel(EntityUid uid)
    {
        if (!TryComp<BloodstreamComponent>(uid, out var stream))
            return 0;

        if (!_solution.ResolveSolution(uid, stream.BloodSolutionName, ref stream.BloodSolution, out var blood))
            return 0;

        float level = 0;

        var paraQty = (float)blood.GetTotalPrototypeQuantity("Paracetamol").Float();
        level += paraQty * PARACETAMOL_PER_UNIT;

        var tramQty = (float)blood.GetTotalPrototypeQuantity("Tramadol").Float();
        level += tramQty * TRAMADOL_PER_UNIT;

        var oxyQty = (float)blood.GetTotalPrototypeQuantity("Oxycodone").Float();
        level += oxyQty * OXYCODONE_PER_UNIT;

        return level;
    }
}

[ByRefEvent]
public record struct PainDropItemEvent(EntityUid uid);

[ByRefEvent]
public record struct PainStunEvent(EntityUid uid);
