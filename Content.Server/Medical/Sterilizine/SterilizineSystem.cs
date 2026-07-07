using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Interaction;
using Content.Shared.Medical.Sterilizine;
using Content.Shared.Medical.Wounds;
using Content.Shared.Popups;

namespace Content.Server.Medical.Sterilizine;

/// <summary>
///     When sprayed or applied to a wound, reduces germ_level by 10 and sets Disinfected flag.
///     Prevents infection during surgery.
/// </summary>
public sealed partial class SterilizineSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SterilizineComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(Entity<SterilizineComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target == null || !args.CanReach)
            return;

        if (!TryComp<BodyComponent>(args.Target.Value, out var body) || body.Organs == null)
            return;

        args.Handled = true;
        var cleaned = 0;

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (!TryComp<WoundableComponent>(organ, out var woundable))
                continue;

            foreach (var woundUid in woundable.Wounds)
            {
                if (TerminatingOrDeleted(woundUid))
                    continue;

                if (!TryComp<WoundGermComponent>(woundUid, out var germ))
                    continue;

                germ.GermLevel = Math.Max(0, germ.GermLevel - 10);
                Dirty(woundUid, germ);
                cleaned++;
            }
        }

        if (cleaned > 0)
            _popup.PopupEntity(Loc.GetString("sterilizine-applied", ("count", cleaned)), args.Target.Value, args.User, PopupType.Medium);
        else
            _popup.PopupEntity(Loc.GetString("sterilizine-no-effect"), args.Target.Value, args.User);
    }
}
