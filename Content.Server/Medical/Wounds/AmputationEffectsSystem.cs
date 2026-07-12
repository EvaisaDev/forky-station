using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organs;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;

namespace Content.Server.Medical.Wounds;

public sealed partial class AmputationEffectsSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyComponent, LimbDismemberedEvent>(OnLimbDismembered);
        SubscribeLocalEvent<BodyComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovespeed);
    }

    private void OnLimbDismembered(Entity<BodyComponent> ent, ref LimbDismemberedEvent args)
    {
        if (TerminatingOrDeleted(args.Limb) || !TryComp<ExternalOrganComponent>(args.Limb, out var ext))
            return;

        if ((ext.Flags & LimbFlags.CanStand) != 0)
        {
            var legsRemaining = CountStandingLimbs(ent);
            if (legsRemaining == 0)
            {
                _stun.TryAddParalyzeDuration(ent, TimeSpan.FromSeconds(4));
                _popup.PopupEntity(Loc.GetString("amputation-no-legs"), ent, ent);
            }
            else if (legsRemaining == 1)
            {
                _stun.TryAddParalyzeDuration(ent, TimeSpan.FromSeconds(1));
                _popup.PopupEntity(Loc.GetString("amputation-one-leg"), ent, ent);
            }
        }

        if ((ext.Flags & LimbFlags.CanGrasp) != 0)
        {
            _popup.PopupEntity(Loc.GetString("amputation-no-grasp", ("limb", Name(args.Limb))), ent, ent);
        }

        _movementSpeed.RefreshMovementSpeedModifiers(ent);
    }

    private void OnRefreshMovespeed(Entity<BodyComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Organs == null)
            return;

        var standCount = CountStandingLimbs(ent);
        if (standCount < 2)
        {
            var speedMod = 0.3f + (standCount * 0.2f);
            args.ModifySpeed(speedMod, speedMod);
        }
    }

    private int CountStandingLimbs(Entity<BodyComponent> ent)
    {
        if (ent.Comp.Organs == null)
            return 0;

        var count = 0;
        foreach (var organ in ent.Comp.Organs.ContainedEntities)
        {
            if (TerminatingOrDeleted(organ))
                continue;

            if (!TryComp<ExternalOrganComponent>(organ, out var ext))
                continue;

            if ((ext.Flags & LimbFlags.CanStand) == 0)
                continue;

            if ((ext.Status & OrganStatusFlags.CutAway) != 0)
                continue;

            count++;
        }

        return count;
    }
}
