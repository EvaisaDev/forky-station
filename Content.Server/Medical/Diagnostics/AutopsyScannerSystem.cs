using System.Text;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organs;
using Content.Shared.Interaction;
using Content.Shared.Medical.Diagnostics;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;

namespace Content.Server.Medical.Diagnostics;

public sealed partial class AutopsyScannerSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AutopsyScannerComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(Entity<AutopsyScannerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target == null || !args.CanReach)
            return;

        args.Handled = true;

        if (!TryComp<MobStateComponent>(args.Target.Value, out var mobState) || !_mobState.IsDead(args.Target.Value, mobState))
        {
            _popup.PopupEntity(Loc.GetString("autopsy-patient-alive"), args.Target.Value, args.User);
            return;
        }

        if (!TryComp<BodyComponent>(args.Target.Value, out var body) || body.Organs == null)
            return;

        var report = new StringBuilder();
        report.AppendLine(Loc.GetString("autopsy-report-header", ("target", Name(args.Target.Value))));

        var hasFindings = false;

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (!TryComp<ExternalOrganComponent>(organ, out var ext))
                continue;

            var limbName = Name(organ);
            var findings = new List<string>();

            if (ext.BruteDamage > 0)
                findings.Add(Loc.GetString("autopsy-finding-brute", ("damage", ext.BruteDamage)));

            if (ext.BurnDamage > 0)
                findings.Add(Loc.GetString("autopsy-finding-burn", ("damage", ext.BurnDamage)));

            if ((ext.Status & OrganStatusFlags.Broken) != 0)
                findings.Add(Loc.GetString("autopsy-finding-fracture"));

            if ((ext.Status & OrganStatusFlags.ArteryCut) != 0)
                findings.Add(Loc.GetString("autopsy-finding-artery"));

            if ((ext.Status & OrganStatusFlags.Bleeding) != 0)
                findings.Add(Loc.GetString("autopsy-finding-bleeding"));

            if (findings.Count > 0)
            {
                hasFindings = true;
                report.AppendLine(Loc.GetString("autopsy-limb-findings", ("limb", limbName)));
                foreach (var f in findings)
                    report.AppendLine("  - " + f);
            }

            foreach (var innerOrgan in body.Organs.ContainedEntities)
            {
                if (innerOrgan == organ)
                    continue;

                if (TryComp<HeartConditionComponent>(innerOrgan, out var heart) && heart.Efficiency < 1.0f)
                {
                    report.AppendLine(Loc.GetString("autopsy-organ-damage", ("organ", Name(innerOrgan))));
                    hasFindings = true;
                }

                if (TryComp<LungConditionComponent>(innerOrgan, out var lung) && lung.Efficiency < 1.0f)
                {
                    report.AppendLine(Loc.GetString("autopsy-organ-damage", ("organ", Name(innerOrgan))));
                    hasFindings = true;
                }
            }
        }

        if (!hasFindings)
            report.AppendLine(Loc.GetString("autopsy-no-findings"));

        _popup.PopupEntity(report.ToString(), args.Target.Value, args.User, PopupType.Large);
    }
}
