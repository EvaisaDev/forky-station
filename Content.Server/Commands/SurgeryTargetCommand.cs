using Content.Shared.Body;
using Content.Shared.Medical.Surgery;
using Robust.Server.GameObjects;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server.Commands;

public sealed partial class SurgeryTargetCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    public string Command => "surgerytarget";
    public string Description => "Sets your surgery target zone or opens the UI. Usage: surgerytarget [zone]";
    public string Help => "surgerytarget <zone> - Sets target zone\nsurgerytarget - Opens target selection UI";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player == null)
        {
            shell.WriteLine("This command can only be used in-game.");
            return;
        }

        var attachedEntity = shell.Player.AttachedEntity;
        if (attachedEntity == null)
        {
            shell.WriteLine("You have no entity.");
            return;
        }

        // No args = open UI
        if (args.Length == 0)
        {
            var uiSystem = _entMan.System<UserInterfaceSystem>();
            var comp = _entMan.EnsureComponent<SurgeryTargetComponent>(attachedEntity.Value);
            uiSystem.OpenUi(attachedEntity.Value, SurgeryTargetUiKey.Key, shell.Player);
            return;
        }

        var zone = args[0];
        zone = char.ToUpper(zone[0]) + zone[1..];

        if (!_prototype.HasIndex<OrganCategoryPrototype>(zone))
        {
            shell.WriteLine($"Unknown target zone: {zone}. Valid zones: Torso, Head, ArmLeft, ArmRight, HandLeft, HandRight, LegLeft, LegRight, FootLeft, FootRight");
            return;
        }

        var targetComp = _entMan.EnsureComponent<SurgeryTargetComponent>(attachedEntity.Value);
        targetComp.TargetZone = zone;
        _entMan.Dirty(attachedEntity.Value, targetComp);

        shell.WriteLine($"Surgery target zone set to: {zone}");
    }
}
