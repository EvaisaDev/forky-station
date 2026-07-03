using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organs;
using Content.Shared.Medical.Wounds;
using Robust.Shared.Console;

namespace Content.Server.Commands;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class MedicalDebugCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entMan = default!;

    public string Command => "medicaldebug";
    public string Description => "Prints medical debug info for a target entity.";
    public string Help => "medicaldebug <entity uid>\nPrints limb damage, wounds, oxygenation, pulse, and brain status.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteLine("Usage: medicaldebug <entity uid>");
            return;
        }

        if (!NetEntity.TryParse(args[0], out var nuid))
        {
            shell.WriteLine("Invalid entity UID.");
            return;
        }

        var uid = _entMan.GetEntity(nuid);

        shell.WriteLine($"=== Medical Debug: {_entMan.GetComponent<MetaDataComponent>(uid).EntityName} ({uid}) ===");

        // Brain status
        if (_entMan.TryGetComponent<BrainComponent>(uid, out var brain))
        {
            shell.WriteLine($"  Brain: {brain.Integrity}/{brain.MaxIntegrity} integrity, HasBeenDead={brain.HasBeenDead}");
        }

        // Oxygenation / pulse / cardiac arrest
        if (_entMan.TryGetComponent<BloodOxygenationComponent>(uid, out var oxy))
        {
            shell.WriteLine($"  O2: {oxy.Oxygenation:P1}, Pulse: {oxy.PulseRate:F0} BPM, Arrest={oxy.CardiacArrest}, BasePulse={oxy.BasePulse}");
        }

        // Blood type
        if (_entMan.TryGetComponent<BloodTypeComponent>(uid, out var bloodType))
        {
            shell.WriteLine($"  Blood: {bloodType.DisplayString}");
        }

        // Bleed amount
        if (_entMan.TryGetComponent<BloodstreamComponent>(uid, out var stream))
        {
            shell.WriteLine($"  BleedAmount: {stream.BleedAmount:F2}, MaxBleed: {stream.MaxBleedAmount}");
        }

        // Limb and wound info
        if (_entMan.TryGetComponent<BodyComponent>(uid, out var body) && body.Organs != null)
        {
            foreach (var organ in body.Organs.ContainedEntities)
            {
                var organName = _entMan.GetComponent<MetaDataComponent>(organ).EntityName;

                if (_entMan.TryGetComponent<ExternalOrganComponent>(organ, out var ext))
                {
                    var limbInfo = $"  Limb [{organName}]: Brute={ext.BruteDamage}, Burn={ext.BurnDamage}, Stage={ext.SurgeryStage}, Status={ext.Status}";
                    if (ext.Dislocated)
                        limbInfo += ", DISLOCATED";
                    shell.WriteLine(limbInfo);
                }

                if (_entMan.TryGetComponent<WoundableComponent>(organ, out var woundable))
                {
                    shell.WriteLine($"    Wounds: {woundable.Wounds.Count}, TotalDmg={woundable.TotalDamage.GetTotal()}");

                    foreach (var woundUid in woundable.Wounds)
                    {
                        if (!_entMan.TryGetComponent<WoundComponent>(woundUid, out var wound))
                        {
                            shell.WriteLine($"      [INVALID WOUND]");
                            continue;
                        }

                        var wName = _entMan.GetComponent<MetaDataComponent>(woundUid).EntityName;
                        var wDmg = wound.Damage.GetTotal();
                        var woundInfo = $"      Wound [{wName}]: dmg={wDmg}, max={wound.MaximumDamage}";

                        if (_entMan.TryGetComponent<BleedingWoundComponent>(woundUid, out var bleed))
                            woundInfo += $", bleed={bleed.CurrentBleedAmount:F1}";

                        if (_entMan.TryGetComponent<TendableWoundComponent>(woundUid, out var tend))
                            woundInfo += $", tended={tend.Tended}";

                        if (_entMan.TryGetComponent<ClampableWoundComponent>(woundUid, out var clamp))
                            woundInfo += $", clamped={clamp.Clamped}";

                        if (_entMan.TryGetComponent<PainfulWoundComponent>(woundUid, out var pain))
                            woundInfo += $", pain={pain.PainAmount}, fresh={pain.FreshPainAmount}";

                        shell.WriteLine(woundInfo);
                    }
                }

                // Check heart/lungs
                if (_entMan.TryGetComponent<HeartConditionComponent>(organ, out var heart))
                    shell.WriteLine($"  Organ [{organName}]: Heart beating={heart.Beating}, eff={heart.Efficiency:P0}");

                if (_entMan.TryGetComponent<LungConditionComponent>(organ, out var lung))
                    shell.WriteLine($"  Organ [{organName}]: Lung eff={lung.Efficiency:P0}");
            }
        }
        else
        {
            shell.WriteLine("  No body component or no organs found.");
        }
    }
}
