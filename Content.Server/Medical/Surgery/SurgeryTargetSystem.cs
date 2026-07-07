using Content.Shared.Medical.Surgery;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server.Medical.Surgery;

public sealed partial class SurgeryTargetSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SurgeryTargetComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<SurgeryTargetComponent, SetSurgeryTargetMessage>(OnSetTarget);
    }

    private void OnUiOpened(Entity<SurgeryTargetComponent> ent, ref BoundUIOpenedEvent args)
    {
        // Update the UI with the current target zone
        if (args.UiKey is SurgeryTargetUiKey)
        {
            // No state to send; the client just shows buttons
        }
    }

    private void OnSetTarget(Entity<SurgeryTargetComponent> ent, ref SetSurgeryTargetMessage args)
    {
        ent.Comp.TargetZone = args.TargetZone;
        Dirty(ent);
    }

    /// <summary>
    /// Opens the surgery target selector UI for a player.
    /// </summary>
    public void OpenUi(EntityUid player)
    {
        var comp = EnsureComp<SurgeryTargetComponent>(player);
        _ui.OpenUi(player, SurgeryTargetUiKey.Key, player);
    }
}
