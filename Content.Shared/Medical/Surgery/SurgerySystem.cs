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
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.Medical.Surgery;

public sealed partial class SurgerySystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedContainerSystem _container = default!;

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

        // Quality check for improvised tools
        if (toolComp.Quality < 1.0f && !_random.Prob(toolComp.Quality))
        {
            _popup.PopupEntity(Loc.GetString("surgery-tool-slipped"), args.User, args.User);
            return;
        }

        if (!IsPatientReady(target))
        {
            _popup.PopupEntity(Loc.GetString("surgery-patient-not-ready"), args.User, args.User);
            return;
        }

        var targetZone = GetSurgeryTargetZone(args.User);

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

            if (organComp.Category == targetZone)
            {
                matchedLimb = organ;
                matchedExt = external;
                break;
            }

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

        var result = ExecuteStep(tool, toolComp, matchedLimb.Value, matchedExt!, body, target);
        if (result != null)
        {
            _popup.PopupEntity(result, args.User, args.User);

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

    private ProtoId<OrganCategoryPrototype> GetSurgeryTargetZone(EntityUid surgeon)
    {
        if (TryComp<TargetingComponent>(surgeon, out var targeting))
            return BodyPartHelper.ToOrganCategory(targeting.ActivePart);

        if (TryComp<SurgeryTargetComponent>(surgeon, out var target))
            return target.TargetZone;

        return "Torso";
    }

    private bool IsPatientReady(EntityUid target)
    {
        if (TryComp<MobStateComponent>(target, out var mobState))
            return mobState.CurrentState != MobState.Alive;

        return true;
    }

    public string? ExecuteStep(EntityUid tool, SurgeryToolComponent toolComp, EntityUid limb, ExternalOrganComponent external, BodyComponent? body = null, EntityUid bodyEnt = default)
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
            if (TryComp<OrganComponent>(limb, out var organComp) && organComp.Body is { } orgBody)
            {
                if (TryComp<BodyComponent>(orgBody, out var bodyComp) && bodyComp.Organs != null)
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

        if (action == "BoneSaw" && external.SurgeryStage == SurgeryStage.Retracted)
        {
            external.SurgeryStage = SurgeryStage.Encased;
            Dirty(limb, external);
            return Loc.GetString("surgery-bone-opened");
        }

        // Organ Transplant: Detach an internal organ for removal
        if (action == "OrganDetach" && CanOperateOnOpen(external) && body != null)
        {
            return DetachOrgan(limb, body, bodyEnt);
        }

        // Organ Transplant: Remove a detached organ from the body entirely
        if (action == "OrganRemove" && CanOperateOnOpen(external) && body != null)
        {
            return RemoveOrgan(limb, body, bodyEnt);
        }

        // Organ Transplant: Insert an organ from the surgeon's hand into the body
        if (action == "OrganReplace" && CanOperateOnOpen(external) && body != null)
        {
            return ReplaceOrgan(tool, body, bodyEnt);
        }

        // Organ Transplant: Attach an organ to the blood supply
        if (action == "OrganAttach" && CanOperateOnOpen(external))
        {
            return AttachOrgan(limb);
        }

        return Loc.GetString("surgery-step-not-valid", ("action", action));
    }

    private string? DetachOrgan(EntityUid limb, BodyComponent body, EntityUid bodyEnt)
    {
        if (body.Organs == null)
            return Loc.GetString("surgery-no-organs");

        // Find first internal organ in this limb's body that isn't the limb itself
        foreach (var innerOrgan in body.Organs.ContainedEntities)
        {
            if (innerOrgan == limb)
                continue;

            if (HasComp<HeartConditionComponent>(innerOrgan)
                || HasComp<LungConditionComponent>(innerOrgan)
                || HasComp<BrainComponent>(innerOrgan))
            {
                // Mark as detached by setting a component or flag
                // For now, just return success message; actual removal happens in OrganRemove
                return Loc.GetString("surgery-organ-detached", ("organ", Name(innerOrgan)));
            }
        }

        return Loc.GetString("surgery-no-organs-in-limb");
    }

    private string? RemoveOrgan(EntityUid limb, BodyComponent body, EntityUid bodyEnt)
    {
        if (body.Organs == null)
            return Loc.GetString("surgery-no-organs");

        foreach (var innerOrgan in body.Organs.ContainedEntities)
        {
            if (innerOrgan == limb)
                continue;

            if (!HasComp<HeartConditionComponent>(innerOrgan)
                && !HasComp<LungConditionComponent>(innerOrgan)
                && !HasComp<BrainComponent>(innerOrgan))
                continue;

            _container.Remove(innerOrgan, body.Organs);
            var xform = Transform(innerOrgan);
            xform.Coordinates = Transform(bodyEnt).Coordinates;

            return Loc.GetString("surgery-organ-removed", ("organ", Name(innerOrgan)));
        }

        return Loc.GetString("surgery-no-organs-in-limb");
    }

    private string? ReplaceOrgan(EntityUid heldOrgan, BodyComponent body, EntityUid bodyEnt)
    {
        if (body.Organs == null)
            return Loc.GetString("surgery-no-organs");

        if (!TryComp<OrganComponent>(heldOrgan, out var organComp))
            return Loc.GetString("surgery-not-an-organ");

        if (organComp.Body != null)
            return Loc.GetString("surgery-organ-still-attached");

        // Insert the organ into the body container
        _container.Insert(heldOrgan, body.Organs);

        return Loc.GetString("surgery-organ-replaced", ("organ", Name(heldOrgan)));
    }

    private string? AttachOrgan(EntityUid limb)
    {
        // Organ is now connected to blood supply - this happens automatically
        // when inserted into the body container via BodySystem events
        return Loc.GetString("surgery-organ-attached");
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
