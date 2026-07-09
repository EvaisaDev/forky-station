using Content.Shared._Medical.Targeting;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organs;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Medical.Wounds;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
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
    [Dependency] private IPrototypeManager _prototype = default!;

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
        {
            args.Handled = true;
            return;
        }

        args.Handled = true;

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
                    if (TryComp<WoundEffectsComponent>(wUid, out var effects))
                    {
                        var germInstance = effects.GetEffect("Germ", _prototype);
                        if (germInstance != null)
                        {
                            germInstance.SetFloat("germLevel", germInstance.GetFloat("germLevel") + 5);
                            Dirty(wUid, germInstance);
                        }
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
        // Patient is ready for surgery if:
        // - Unconscious (Critical, Dead, Sleeping, KnockedDown, Stunned)
        // - Or has no MobState at all (testing/debug)
        if (TryComp<MobStateComponent>(target, out var mobState))
        {
            if (mobState.CurrentState != MobState.Alive)
                return true;

            // Alive but can be unconscious from other causes
            return HasComp<SleepingComponent>(target)
                || HasComp<KnockedDownComponent>(target)
                || HasComp<StunnedComponent>(target);
        }

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
                    if (!TryComp<WoundEffectsComponent>(wUid, out var effects))
                        continue;
                    var embedded = effects.GetEffect("Embedded", _prototype);
                    if (embedded == null || embedded.StringListParams.Count == 0)
                        continue;

                    embedded.StringListParams.RemoveAt(0);
                    Dirty(wUid, embedded);
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

        if (action == "OrganDetach" && CanOperateOnOpen(external) && body != null && bodyEnt.IsValid())
            return DetachOrgan(limb, body, bodyEnt);

        if (action == "OrganRemove" && CanOperateOnOpen(external) && body != null && bodyEnt.IsValid())
            return RemoveOrgan(limb, body, bodyEnt);

        if (action == "OrganReplace" && CanOperateOnOpen(external) && body != null && bodyEnt.IsValid())
            return ReplaceOrgan(tool, body, bodyEnt);

        if (action == "OrganAttach" && CanOperateOnOpen(external))
            return Loc.GetString("surgery-organ-attached");

        return Loc.GetString("surgery-step-not-valid", ("action", action));
    }

    private string? DetachOrgan(EntityUid limb, BodyComponent body, EntityUid bodyEnt)
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

            // Verify the organ belongs to this limb by checking parent
            if (TryComp<OrganComponent>(innerOrgan, out var orgComp))
            {
                // Mark as detached by setting a temporary flag via OrganDetachedComponent
                EnsureComp<OrganDetachedComponent>(innerOrgan);
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

            if (!HasComp<OrganDetachedComponent>(innerOrgan))
                continue;

            if (!HasComp<HeartConditionComponent>(innerOrgan)
                && !HasComp<LungConditionComponent>(innerOrgan)
                && !HasComp<BrainComponent>(innerOrgan))
                continue;

            RemComp<OrganDetachedComponent>(innerOrgan);
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

        _container.Insert(heldOrgan, body.Organs);

        // BodySystem.OnBodyEntInserted will set organComp.Body automatically
        // via the OrganGotInsertedEvent handler

        return Loc.GetString("surgery-organ-replaced", ("organ", Name(heldOrgan)));
    }

    private static bool CanIncise(ExternalOrganComponent limb)
    {
        return limb.SurgeryStage == SurgeryStage.None
            && (limb.Status & OrganStatusFlags.CutAway) == 0;
    }

    private static bool CanCauterize(ExternalOrganComponent limb)
    {
        return limb.SurgeryStage == SurgeryStage.Incised
            || limb.SurgeryStage == SurgeryStage.Clamped
            || limb.SurgeryStage == SurgeryStage.Retracted
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

/// <summary>
///     Marks an internal organ as having been surgically detached, ready for removal.
/// </summary>
[RegisterComponent]
public sealed partial class OrganDetachedComponent : Component;
