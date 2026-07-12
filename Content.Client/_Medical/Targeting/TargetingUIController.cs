using Content.Client.Gameplay;
using Content.Shared._Medical.Targeting;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._Medical.Targeting;

public sealed class TargetingUIController : UIController, IOnStateEntered<GameplayState>, IOnSystemChanged<TargetingSystem>
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IEntityNetworkManager _net = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    private TargetingComponent? _targetingComponent;
    private TargetingControl? TargetingControl => UIManager.GetActiveUIWidgetOrNull<TargetingControl>();

    public void OnSystemLoaded(TargetingSystem system)
    {
        system.TargetingStartup += AddTargetingControl;
        system.TargetingShutdown += RemoveTargetingControl;
        system.TargetChange += CycleTarget;
    }

    public void OnSystemUnloaded(TargetingSystem system)
    {
        system.TargetingStartup -= AddTargetingControl;
        system.TargetingShutdown -= RemoveTargetingControl;
        system.TargetChange -= CycleTarget;
    }

    public void OnStateEntered(GameplayState state)
    {
        if (TargetingControl == null)
            return;

        TargetingControl.SetTargetDollVisible(_targetingComponent != null);

        if (_targetingComponent != null)
            TargetingControl.SetBodyPartsVisible(_targetingComponent.ActivePart);
    }

    public void AddTargetingControl(TargetingComponent component)
    {
        _targetingComponent = component;

        var control = TargetingControl;
        if (control != null)
        {
            control.OnPartSelected += CycleTarget;
            control.SetTargetDollVisible(_targetingComponent != null);

            if (_targetingComponent != null)
                control.SetBodyPartsVisible(_targetingComponent.ActivePart);
        }
    }

    public void RemoveTargetingControl()
    {
        var control = TargetingControl;
        if (control != null)
        {
            control.OnPartSelected -= CycleTarget;
            control.SetTargetDollVisible(false);
        }

        _targetingComponent = null;
    }

    public void CycleTarget(TargetBodyPart bodyPart)
    {
        if (_playerManager.LocalEntity is not { } user || TargetingControl == null)
            return;

        var msg = new TargetChangeEvent(_entManager.GetNetEntity(user), bodyPart);
        _net.SendSystemNetworkMessage(msg);
        TargetingControl?.SetBodyPartsVisible(bodyPart);
    }
}
