using System.Text;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Medical.Diagnostics;
using Content.Shared.Popups;

namespace Content.Server.Medical.Diagnostics;

public sealed partial class MassSpectrometerSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MassSpectrometerComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(Entity<MassSpectrometerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target == null || !args.CanReach)
            return;

        args.Handled = true;

        if (!TryComp<SolutionManagerComponent>(args.Target.Value, out var solutionManager))
            return;

        if (!_solutionContainer.TryGetSolution(args.Target.Value, "default", out var solutionEnt, out var solution))
        {
            _popup.PopupEntity(Loc.GetString("mass-spectrometer-no-solution"), args.Target.Value, args.User);
            return;
        }

        if (solution.Contents.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("mass-spectrometer-empty"), args.Target.Value, args.User);
            return;
        }

        var report = new StringBuilder();
        report.AppendLine(Loc.GetString("mass-spectrometer-header", ("target", Name(args.Target.Value))));

        foreach (var (reagent, quantity) in solution.Contents)
        {
            var reagentName = reagent.Prototype;
            report.AppendLine(Loc.GetString("mass-spectrometer-reagent",
                ("name", reagentName),
                ("amount", quantity)));
        }

        _popup.PopupEntity(report.ToString(), args.Target.Value, args.User, PopupType.Large);
    }
}
