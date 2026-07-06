using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared.Medical.CPR;

public sealed partial class CPRSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private BloodOxygenationSystem _bloodOxygenation = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CPRComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<CPRComponent, CPRDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<CPRComponent, InteractHandEvent>(OnInteractHand);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<CPRComponent>();

        while (query.MoveNext(out var uid, out var cpr))
        {
            if (!cpr.Active)
                continue;

            if (curTime < cpr.ExpiryTime)
                continue;

            cpr.Active = false;
            cpr.Performer = null;
            Dirty(uid, cpr);

            if (TryComp<BloodOxygenationComponent>(uid, out var oxygenation) && oxygenation.CardiacArrest)
                _popup.PopupEntity(Loc.GetString("cpr-expired", ("target", uid)), uid, PopupType.Medium);
        }
    }

    private void OnInit(Entity<CPRComponent> ent, ref ComponentInit args)
    {
        ent.Comp.ExpiryTime = _timing.CurTime;
    }

    private void OnInteractHand(Entity<CPRComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<BloodOxygenationComponent>(ent, out var oxygenation))
            return;

        if (!oxygenation.CardiacArrest)
        {
            _popup.PopupEntity(Loc.GetString("cpr-not-needed", ("target", ent)), ent, args.User);
            return;
        }

        args.Handled = true;

        var doAfterArgs = new DoAfterArgs(EntityManager, args.User, ent.Comp.Duration, new CPRDoAfterEvent(), ent, target: ent, used: args.User)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnDoAfter(Entity<CPRComponent> ent, ref CPRDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        if (!TryComp<BloodOxygenationComponent>(target, out var oxygenation))
            return;

        if (!oxygenation.CardiacArrest)
            return;

        ent.Comp.Active = true;
        ent.Comp.Performer = args.User;
        ent.Comp.ExpiryTime = _timing.CurTime + ent.Comp.Duration;
        Dirty(target, ent.Comp);

        _popup.PopupEntity(Loc.GetString("cpr-performing", ("performer", Identity.Entity(args.User, EntityManager)), ("target", target)), target, PopupType.Medium);

        args.Handled = true;
    }
}

[Serializable, NetSerializable]
public sealed partial class CPRDoAfterEvent : SimpleDoAfterEvent;
