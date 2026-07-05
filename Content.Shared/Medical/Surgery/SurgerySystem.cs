using Content.Shared._Medical.Targeting;
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
using Robust.Shared.Prototypes;
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

        if (!IsPatientReady(target))
        {
            _popup.PopupEntity(Loc.GetString("surgery-patient-not-ready"), args.User, args.User);
            return;
        }

        // Find the target zone from the surgeon's SurgeryTargetComponent
        var targetZone = GetSurgeryTargetZone(args.User);

        // Find the matching limb
        EntityUid? matchedLimb = null;
        ExternalOrganComponent? matchedExt = null;

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (!TryComp<WoundableComponent>(organ, out _))
                continue;

            if (!TryComp<ExternalOrganComponent>(organ, out var external))
                continue;

            if (!TryComp<OrganComponent>(organ, out var organComp))
                continue;

            // Match by category
            if (organComp.Category == targetZone)
            {
                matchedLimb = organ;
                matchedExt = external;
                break;
            }

            // Fallback: if no match, use torso
            if (matchedLimb == null && organComp.Category == "Torso")
            {
                matchedLimb = organ;
                matchedExt = external;
            }
        }

        if (matchedLimb == null)
        {
            _popup.PopupEntity(Loc.GetString("surgery-no-woundable-limb"), args.User, args.User);
            return;
        }

        var result = ExecuteStep(tool, toolComp, matchedLimb.Value, matchedExt!);
        if (result != null)
        {
            _popup.PopupEntity(result, args.User, args.User);

            // Non-sterile tools increase wound infection risk
            if (!toolComp.Sterile && TryComp<WoundableComponent>(matchedLimb.Value, out var wnd))
            {
                foreach (var wUid in wnd.Wounds)
                {
                    if (TryComp<WoundGermComponent>(wUid, out var germ))
                    {
                        germ.GermLevel += 5;
                        Dirty(wUid, germ);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Gets the surgeon's target zone from their TargetingComponent.
    ///     Defaults to "Torso" if not set.
    /// </summary>
    private ProtoId<OrganCategoryPrototype> GetSurgeryTargetZone(EntityUid surgeon)
    {
        if (TryComp<TargetingComponent>(surgeon, out var targeting))
            return BodyPartHelper.ToOrganCategory(targeting.ActivePart);

        // Fallback to old SurgeryTargetComponent if available
        if (TryComp<SurgeryTargetComponent>(surgeon, out var target))
            return target.TargetZone;

        return "Torso";
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
                // 3-step bone repair: BoneGlue(1) -> BoneSet(2) -> BoneGlue(3) = complete
                if (action == "BoneGlue" && external.BoneRepairStage == 2)
                {
                    external.BoneRepairStage = 3;
                    external.Status &= ~OrganStatusFlags.Broken;
                    Dirty(limb, external);
                    return Loc.GetString("surgery-bone-repaired");
                }
                else if (action == "BoneGlue" && external.BoneRepairStage != 2)
                {
                    external.BoneRepairStage = 1;
                    Dirty(limb, external);
                    return Loc.GetString("surgery-bone-glued");
                }
                else if (action == "BoneSet" && external.BoneRepairStage == 1)
                {
                    external.BoneRepairStage = 2;
                    Dirty(limb, external);
                    return Loc.GetString("surgery-bone-set");
                }
                return Loc.GetString("surgery-step-not-valid", ("action", action));
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

        // Remove embedded objects (shrapnel, bullets) from wounds
        if (action == "RemoveEmbedded" && CanOperateOnOpen(external))
        {
            if (TryComp<WoundableComponent>(limb, out var wnd))
            {
                var removed = 0;
                foreach (var wUid in wnd.Wounds)
                {
                    if (!TryComp<EmbeddedObjectComponent>(wUid, out var emb))
                        continue;
                    if (emb.EmbeddedItems.Count == 0)
                        continue;

                    emb.EmbeddedItems.RemoveAt(0);
                    Dirty(wUid, emb);
                    removed++;
                }

                if (removed > 0)
                    return Loc.GetString("surgery-embedded-removed");
            }
            return Loc.GetString("surgery-no-embedded");
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
