using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Interaction;
using Content.Shared.Medical.Diagnostics;
using Content.Shared.Popups;
using Content.Shared.Timing;

namespace Content.Server.Medical.Diagnostics;

public sealed partial class PenlightSystem : EntitySystem
{
    [Dependency] private BrainSystem _brain = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PenlightComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(Entity<PenlightComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target == null || !args.CanReach)
            return;

        args.Handled = true;

        if (!TryComp<BodyComponent>(args.Target.Value, out var body) || body.Organs == null)
            return;

        EntityUid? brainOrgan = null;
        float brainPct = 1.0f;

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (TryComp<BrainComponent>(organ, out var brain))
            {
                brainOrgan = organ;
                brainPct = _brain.GetBrainIntegrityPercent((organ, brain));
                break;
            }
        }

        if (brainOrgan == null)
        {
            _popup.PopupEntity(Loc.GetString("penlight-no-brain"), args.Target.Value, args.User);
            return;
        }

        string msg;
        if (brainPct <= 0f)
            msg = Loc.GetString("penlight-brain-dead");
        else if (brainPct < 0.3f)
            msg = Loc.GetString("penlight-brain-severe");
        else if (brainPct < 0.6f)
            msg = Loc.GetString("penlight-brain-moderate");
        else if (brainPct < 0.9f)
            msg = Loc.GetString("penlight-brain-minor");
        else
            msg = Loc.GetString("penlight-brain-normal");

        _popup.PopupEntity(msg, args.Target.Value, args.User, PopupType.Medium);
    }
}
