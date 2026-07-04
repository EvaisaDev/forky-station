using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organs;
using Content.Shared.Interaction;
using Content.Shared.Medical.Wounds;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Shared.Medical.Surgery;

/// <summary>
///     Core surgery system. Handles tool interaction on woundable body parts.
/// </summary>
public sealed partial class SurgerySystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SurgeryToolComponent, AfterInteractEvent>(OnToolAfterInteract);
    }

    private void OnToolAfterInteract(Entity<SurgeryToolComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;

        var target = args.Target.Value;
        var (tool, toolComp) = ent;

        if (!TryComp<BodyComponent>(target, out var body) || body.Organs == null)
            return;

        args.Handled = true;

        // Check anesthesia requirement: patient must be unconscious for surgery
        if (!IsPatientReady(target))
        {
            _popup.PopupEntity(Loc.GetString("surgery-patient-not-ready"), args.User, args.User);
            return;
        }

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (!TryComp<WoundableComponent>(organ, out _))
                continue;

            if (!TryComp<ExternalOrganComponent>(organ, out var external))
                continue;

            var result = ExecuteStep(tool, toolComp, organ, external);
            if (result != null)
                _popup.PopupEntity(result, args.User, args.User);
            return;
        }

        _popup.PopupEntity(Loc.GetString("surgery-no-woundable-limb"), args.User, args.User);
    }

    /// <summary>
    ///     Check if the patient is ready for surgery.
    ///     Baystation requires: patient is unconscious (anesthetized or in crit/dead).
    /// </summary>
    private bool IsPatientReady(EntityUid target)
    {
        // Patient is ready if: unconscious, in critical state, or dead
        if (TryComp<MobStateComponent>(target, out var mobState))
        {
            return mobState.CurrentState != MobState.Alive;
        }

        // If no mob state, allow surgery anyway (for testing/debug)
        return true;
    }

    /// <summary>
    ///     Executes the appropriate surgery step based on tool and limb state.
    ///     Public for testing.
    /// </summary>
    public string? ExecuteStep(EntityUid tool, SurgeryToolComponent toolComp, EntityUid limb, ExternalOrganComponent external)
    {
        var action = toolComp.Action;

        if (action == "Incision" && CanIncise(external))
        {
            external.SurgeryStage = SurgeryStage.Incised;
            Dirty(limb, external);
            return Loc.GetString("surgery-incision-made");
        }

        if (action == "Clamp" && external.SurgeryStage == SurgeryStage.Incised)
        {
            external.SurgeryStage = SurgeryStage.Clamped;
            Dirty(limb, external);
            return Loc.GetString("surgery-bleeders-clamped");
        }

        if (action == "Retract" && external.SurgeryStage == SurgeryStage.Clamped)
        {
            external.SurgeryStage = SurgeryStage.Retracted;
            Dirty(limb, external);
            return Loc.GetString("surgery-skin-retracted");
        }

        if (action == "Cauterize" && CanCauterize(external))
        {
            external.SurgeryStage = SurgeryStage.None;
            Dirty(limb, external);
            return Loc.GetString("surgery-incision-cauterized");
        }

        if (action == "Amputate")
        {
            var ev = new LimbAmputateEvent((limb, external), DropLimbType.Edge);
            RaiseLocalEvent(limb, ref ev);
            return Loc.GetString("surgery-limb-amputated");
        }

        if (action is "BoneGlue" or "BoneSet" && CanOperateOnOpen(external))
        {
            if ((external.Status & OrganStatusFlags.Broken) != 0)
            {
                external.Status &= ~OrganStatusFlags.Broken;
                Dirty(limb, external);
                return Loc.GetString("surgery-bone-repaired");
            }
            return Loc.GetString("surgery-bone-not-broken");
        }

        if (action == "OrganFix" && CanOperateOnOpen(external))
        {
            // Heal damaged internal organs — find the body via the limb's parent
            if (TryComp<OrganComponent>(limb, out var organComp) && organComp.Body is { } body)
            {
                if (TryComp<BodyComponent>(body, out var bodyComp) && bodyComp.Organs != null)
                {
                    foreach (var innerOrgan in bodyComp.Organs.ContainedEntities)
                    {
                        if (TryComp<HeartConditionComponent>(innerOrgan, out var heart))
                        {
                            heart.Efficiency = Math.Min(1.0f, heart.Efficiency + 0.2f);
                            Dirty(innerOrgan, heart);
                        }
                        if (TryComp<LungConditionComponent>(innerOrgan, out var lung))
                        {
                            lung.Efficiency = Math.Min(1.0f, lung.Efficiency + 0.2f);
                            Dirty(innerOrgan, lung);
                        }
                    }
                }
            }
            return Loc.GetString("surgery-organ-repaired");
        }

        if (action == "BoneSaw" && CanAmputate(external))
        {
            var sawEv = new LimbAmputateEvent((limb, external), DropLimbType.Edge);
            RaiseLocalEvent(limb, ref sawEv);
            return Loc.GetString("surgery-limb-amputated");
        }

        // BoneSaw on opened limb = open encasement (ribcage/skull)
        if (action == "BoneSaw" && external.SurgeryStage == SurgeryStage.Retracted)
        {
            external.SurgeryStage = SurgeryStage.Encased;
            Dirty(limb, external);
            return Loc.GetString("surgery-bone-opened");
        }

        return Loc.GetString("surgery-step-not-valid", ("action", action));
    }

    private static bool CanIncise(ExternalOrganComponent limb)
    {
        return limb.SurgeryStage == SurgeryStage.None
            && (limb.Status & OrganStatusFlags.CutAway) == 0;
    }

    private static bool CanCauterize(ExternalOrganComponent limb)
    {
        return limb.SurgeryStage == SurgeryStage.Retracted
            || limb.SurgeryStage == SurgeryStage.Encased;
    }

    private static bool CanOperateOnOpen(ExternalOrganComponent limb)
    {
        return limb.SurgeryStage == SurgeryStage.Retracted
            || limb.SurgeryStage == SurgeryStage.Encased;
    }

    private static bool CanAmputate(ExternalOrganComponent limb)
    {
        return (limb.Flags & LimbFlags.CanAmputate) != 0
            && limb.SurgeryStage == SurgeryStage.None;
    }
}
