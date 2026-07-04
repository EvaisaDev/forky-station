using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organs;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.Medical.Pain;

/// <summary>
///     Calculates shock level from wounds + limb damage - painkillers.
///     Shock is consumed by BloodOxygenationSystem to affect pulse rate.
///     Fresh pain from new wounds decays at 0.05/sec.
/// </summary>
public sealed partial class PainSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private INetManager _net = default!;

    private const float PARACETAMOL_PAINKILL = 35f;
    private const float TRAMADOL_PAINKILL = 50f;
    private const float OXYCODONE_PAINKILL = 80f;

    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PainComponent, MapInitEvent>(OnMapInit);
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

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

            // 1. Decay fresh pain
            pain.FreshPain = Math.Max(0, pain.FreshPain - pain.FreshPainDecay * frameTime);

            // 2. Collect wound pain (including fresh)
            var woundPain = GetWoundPain(body);

            // 3. Collect limb damage pain
            var limbPain = GetLimbDamagePain(body);

            // 4. Check painkiller level from chems
            var painkillerLevel = GetPainkillerLevel(uid);

            // 5. Calculate shock
            var rawPain = woundPain + limbPain + pain.FreshPain;
            pain.PainkillerLevel = painkillerLevel;
            pain.ShockLevel = Math.Max(0, rawPain - painkillerLevel);
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

        if (blood.GetTotalPrototypeQuantity("Paracetamol") > 0)
            level += PARACETAMOL_PAINKILL;

        if (blood.GetTotalPrototypeQuantity("Tramadol") > 0)
            level += TRAMADOL_PAINKILL;

        if (blood.GetTotalPrototypeQuantity("Oxycodone") > 0)
            level += OXYCODONE_PAINKILL;

        return level;
    }
}
