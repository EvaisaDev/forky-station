using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organs;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.Medical.Pain;

public sealed partial class PainSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private INetManager _net = default!;

    // Painkiller effectiveness per unit of drug in the bloodstream
    private const float PARACETAMOL_PER_UNIT = 2.5f;  // 35 at 14u (full therapeutic dose)
    private const float TRAMADOL_PER_UNIT = 3.5f;     // 50 at ~14u
    private const float OXYCODONE_PER_UNIT = 5.5f;    // 80 at ~14.5u

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

            pain.FreshPain = Math.Max(0, pain.FreshPain - pain.FreshPainDecay * (float)UpdateInterval.TotalSeconds);

            var woundPain = GetWoundPain(body);
            var limbPain = GetLimbDamagePain(body);
            var painkillerLevel = GetPainkillerLevel(uid);

            var rawPain = woundPain + limbPain + pain.FreshPain;
            pain.PainkillerLevel = painkillerLevel;
            var prevShock = pain.ShockLevel;
            pain.ShockLevel = Math.Max(0, rawPain - painkillerLevel);

            if (!_net.IsServer)
                continue;

            if (pain.ShockLevel > 80 && prevShock <= 80)
                _popup.PopupEntity(Loc.GetString("pain-extreme", ("target", uid)), uid, uid, PopupType.MediumCaution);
            else if (pain.ShockLevel > 50 && prevShock <= 50)
                _popup.PopupEntity(Loc.GetString("pain-severe", ("target", uid)), uid, uid, PopupType.MediumCaution);
            else if (pain.ShockLevel > 30 && prevShock <= 30)
                _popup.PopupEntity(Loc.GetString("pain-moderate", ("target", uid)), uid, uid, PopupType.MediumCaution);
        }
    }

    private void OnMapInit(Entity<PainComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.ShockLevel = 0;
        ent.Comp.PainkillerLevel = 0;
        ent.Comp.FreshPain = 0;
        ent.Comp.NextUpdate = _timing.CurTime + UpdateInterval;
    }

    private float GetWoundPain(BodyComponent body)
    {
        float total = 0;
        if (body.Organs == null)
            return 0;

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (!TryComp<WoundableComponent>(organ, out var woundable))
                continue;

            foreach (var woundUid in woundable.Wounds)
            {
                if (TerminatingOrDeleted(woundUid))
                    continue;

                var ev = new GetPainEvent(FixedPoint2.Zero, FixedPoint2.Zero);
                RaiseLocalEvent(woundUid, ref ev);
                total += (float)(ev.PainAmount + ev.FreshPainAmount).Float();
            }
        }

        return total;
    }

    private float GetLimbDamagePain(BodyComponent body)
    {
        float total = 0;
        if (body.Organs == null)
            return 0;

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (!TryComp<ExternalOrganComponent>(organ, out var ext))
                continue;

            total += (float)ext.BruteDamage.Float() * 0.5f;
            total += (float)ext.BurnDamage.Float() * 0.3f;

            if ((ext.Status & OrganStatusFlags.Broken) != 0)
                total += 15f;

            if ((ext.Status & OrganStatusFlags.ArteryCut) != 0)
                total += 10f;

            if ((ext.Status & OrganStatusFlags.TendonCut) != 0)
                total += 5f;
        }

        return total;
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
