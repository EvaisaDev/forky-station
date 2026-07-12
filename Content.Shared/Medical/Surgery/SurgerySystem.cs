using Content.Shared._Medical.Targeting;
using Content.Shared.Body;
using Content.Shared.Chat;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organs;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Medical.Pain;
using Content.Shared.Medical.Wounds;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.Medical.Surgery;

public sealed partial class SurgerySystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ISharedChatManager _chat = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SurgeryToolComponent, AfterInteractEvent>(OnToolAfterInteract);
        SubscribeLocalEvent<SurgeryToolComponent, SurgeryStepDoAfterEvent>(OnStepDoAfter);
    }

    public EntityUid? LastSurgeryTarget;
    public GameTick LastSurgeryTick;

    private void OnToolAfterInteract(Entity<SurgeryToolComponent> ent, ref AfterInteractEvent args)
    {
        if (!_net.IsServer)
            return;

        args.Handled = true;

        if (!args.CanReach || args.Target == null)
            return;

        var target = args.Target.Value;
        var (tool, toolComp) = ent;

        if (!TryComp<BodyComponent>(target, out var body) || body.Organs == null)
            return;

        var targetZone = GetSurgeryTargetZone(args.User);
        var (foundLimb, foundExt) = FindTargetLimb(body, targetZone);

        if (foundLimb == null)
        {
            _popup.PopupEntity(Loc.GetString("surgery-no-woundable-limb"), args.User, args.User);
            return;
        }

        // Find step prototype for timed surgery
        var stepProto = FindStepByAction(toolComp.Action);
        if (stepProto != null)
        {
            var conscious = !IsPatientReady(target);
            BeginSurgeryStep(args.User, tool, toolComp, foundLimb.Value, foundExt!, target, body, stepProto, conscious);
        }
        else
        {
            // Fallback to instant execution if no prototype exists
            var result = ExecuteStepAction(tool, toolComp, foundLimb.Value, foundExt!, target);
            if (result != null)
            {
                _popup.PopupEntity(result, args.User, args.User);
                _chat.SendAdminAlert(target, result);
            }
        }
    }

    private SurgeryStepPrototype? FindStepByAction(string action)
    {
        foreach (var proto in _prototype.EnumeratePrototypes<SurgeryStepPrototype>())
        {
            if (proto.Action == action)
                return proto;
        }
        return null;
    }

    private bool AssessBodyPart(SurgeryStepPrototype step, ExternalOrganComponent ext)
    {
        var flags = step.RequiredFlags;

        if ((flags & SurgeryStepFlags.NoRobotic) != 0 && (ext.Status & OrganStatusFlags.Robotic) != 0)
            return false;

        if ((flags & SurgeryStepFlags.NoCrystal) != 0 && (ext.Status & OrganStatusFlags.Brittle) != 0)
            return false;

        if ((flags & SurgeryStepFlags.NoStump) != 0 && (ext.Status & OrganStatusFlags.CutAway) != 0)
            return false;

        return true;
    }

    private bool AssessPatientCondition(string action, EntityUid limb, ExternalOrganComponent ext)
    {
        switch (action)
        {
            case "Clamp":
                return ext.SurgeryStage == SurgeryStage.Incised;
            case "Retract":
                return ext.SurgeryStage == SurgeryStage.Clamped;
            case "SawBone":
                return ext.SurgeryStage == SurgeryStage.Retracted && !string.IsNullOrEmpty(ext.Encased);
            case "BoneGlue":
                return (ext.Status & OrganStatusFlags.Broken) != 0 && ext.BoneRepairStage == 0;
            case "BoneSet":
                return (ext.Status & OrganStatusFlags.Broken) != 0 && ext.BoneRepairStage == 1;
            case "BoneFinish":
                return (ext.Status & OrganStatusFlags.Broken) != 0 && ext.BoneRepairStage == 2;
            case "OrganDetach":
            case "OrganRemove":
            case "OrganReplace":
            case "OrganAttach":
            case "OrganFix":
            case "RemoveEmbedded":
                return ext.SurgeryStage == SurgeryStage.Retracted || ext.SurgeryStage == SurgeryStage.Encased;
            case "Cauterize":
                return ext.SurgeryStage != SurgeryStage.None || (ext.Status & OrganStatusFlags.ArteryCut) != 0;
            default:
                return true;
        }
    }

    private bool IsBlockedByClothing(EntityUid user, EntityUid target, ProtoId<OrganCategoryPrototype> targetZone)
    {
        // Check if the target has thick material covering the surgery zone
        // For now, simplified — skip advanced inventory checks
        return false;
    }

    private void BeginSurgeryStep(EntityUid user, EntityUid tool, SurgeryToolComponent toolComp,
        EntityUid limb, ExternalOrganComponent ext, EntityUid target, BodyComponent body,
        SurgeryStepPrototype stepProto, bool conscious = true)
    {
        // Mark limb as having surgery in progress
        EnsureComp<SurgeryInProgressComponent>(limb);

        // Begin step — messages and initial effects
        var beginMsg = BeginStepEffects(tool, toolComp, limb, ext, target, stepProto);
        if (beginMsg != null)
            _popup.PopupEntity(beginMsg, user, user);

        // Calculate success chance
        var successChance = CalculateSuccessChance(toolComp, stepProto, limb, ext, target, user, conscious);

        // Start do_after
        var duration = _random.NextFloat(stepProto.MinDuration, stepProto.MaxDuration);
        var doAfterArgs = new DoAfterArgs(EntityManager, user, duration,
            new SurgeryStepDoAfterEvent
            {
                User = user,
                Tool = tool,
                StepId = stepProto.ID,
                SuccessChance = successChance,
                Limb = limb
            }, tool, target, tool)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private string? BeginStepEffects(EntityUid tool, SurgeryToolComponent toolComp,
        EntityUid limb, ExternalOrganComponent ext, EntityUid target, SurgeryStepPrototype stepProto)
    {
        var targetName = Name(limb);
        var toolName = Name(tool);
        var action = stepProto.Action;

        // Add shock to patient
        if (stepProto.ShockLevel > 0 && TryComp<WoundableComponent>(limb, out var woundable))
        {
            _pain.AddPainSpike((limb, woundable), 0, stepProto.ShockLevel);
        }

        // Add infection risk from non-sterile tools
        if (stepProto.CanInfect && !toolComp.Sterile && TryComp<WoundableComponent>(limb, out var wnd))
        {
            foreach (var wUid in wnd.Wounds)
            {
                if (TryComp<WoundEffectsComponent>(wUid, out var effects))
                {
                    var germ = effects.GetEffect("Germ", _prototype);
                    if (germ != null)
                    {
                        germ.SetFloat("germLevel", germ.GetFloat("germLevel") + 5);
                        Dirty(wUid, effects);
                    }
                }
            }
        }

        return action switch
        {
            "Incision" => Loc.GetString("surgery-incision-start", ("tool", toolName), ("target", targetName)),
            "LaserIncision" => Loc.GetString("surgery-incision-laser-start", ("tool", toolName), ("target", targetName)),
            "ManagedIncision" => Loc.GetString("surgery-incision-managed-start", ("tool", toolName), ("target", targetName)),
            "Clamp" => Loc.GetString("surgery-clamp-start", ("tool", toolName), ("target", targetName)),
            "Retract" => Loc.GetString("surgery-retract-start", ("tool", toolName), ("target", targetName)),
            "Cauterize" => Loc.GetString("surgery-cauterize-start", ("tool", toolName), ("target", targetName)),
            "SawBone" => Loc.GetString("surgery-saw-start", ("tool", toolName), ("target", targetName)),
            "BoneGlue" => Loc.GetString("surgery-boneglue-start", ("tool", toolName), ("target", targetName)),
            "BoneSet" => Loc.GetString("surgery-boneset-start", ("tool", toolName), ("target", targetName)),
            "BoneFinish" => Loc.GetString("surgery-boneglue-start", ("tool", toolName), ("target", targetName)),
            "Amputate" => Loc.GetString("surgery-amputate-start", ("tool", toolName), ("target", targetName)),
            "OrganDetach" => Loc.GetString("surgery-organ-detach-start", ("tool", toolName), ("target", targetName)),
            "OrganRemove" => Loc.GetString("surgery-organ-remove-start", ("tool", toolName), ("target", targetName)),
            "OrganReplace" => Loc.GetString("surgery-organ-replace-start", ("tool", toolName), ("target", targetName)),
            "OrganAttach" => Loc.GetString("surgery-organ-attach-start", ("tool", toolName), ("target", targetName)),
            "OrganFix" => Loc.GetString("surgery-organ-fix-start", ("tool", toolName), ("target", targetName)),
            "RemoveEmbedded" => Loc.GetString("surgery-embedded-start", ("tool", toolName), ("target", targetName)),
            _ => null
        };
    }

    private float CalculateSuccessChance(SurgeryToolComponent toolComp, SurgeryStepPrototype stepProto,
        EntityUid limb, ExternalOrganComponent ext, EntityUid target, EntityUid user, bool conscious)
    {
        var chance = toolComp.Quality * 100f;

        // Self-surgery penalty
        if (user == target)
            chance -= 10;

        // Conscious patient penalty
        if (!conscious)
            chance -= 15;

        // Surface quality bonus
        if (stepProto.Delicate)
        {
            if (TryComp<OperativeTableComponent>(target, out var table))
                chance *= table.SurgeryQualityBonus;
            else
                chance -= 10;
        }

        return Math.Clamp(chance, 0, 100);
    }

    private void OnStepDoAfter(Entity<SurgeryToolComponent> ent, ref SurgeryStepDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;

        if (!TryComp<ExternalOrganComponent>(args.Limb, out var ext))
            return;

        // Remove surgery-in-progress marker
        RemComp<SurgeryInProgressComponent>(args.Limb);

        if (!_prototype.TryIndex(args.StepId, out var stepProto))
            return;

        var target = args.Target ?? args.Limb;
        var success = _random.Prob(args.SuccessChance / 100f);
        if (success)
        {
            EndSurgeryStep(args.User, args.Tool, ent.Comp, args.Limb, ext, target, stepProto);
        }
        else
        {
            FailSurgeryStep(args.User, args.Tool, ent.Comp, args.Limb, ext, target, stepProto);
        }
    }

    private void EndSurgeryStep(EntityUid user, EntityUid tool, SurgeryToolComponent toolComp,
        EntityUid limb, ExternalOrganComponent ext, EntityUid target,
        SurgeryStepPrototype stepProto)
    {
        var result = ExecuteStepAction(tool, toolComp, limb, ext, target);
        if (result != null)
            _popup.PopupEntity(result, user, user);
    }

    private void FailSurgeryStep(EntityUid user, EntityUid tool, SurgeryToolComponent toolComp,
        EntityUid limb, ExternalOrganComponent ext, EntityUid target,
        SurgeryStepPrototype stepProto)
    {
        var failMsg = GetFailMessage(stepProto.Action, limb, ext);
        if (failMsg != null)
            _popup.PopupEntity(failMsg, user, user);

        ApplyFailDamage(stepProto.Action, limb, ext, tool);
    }

    private string? GetFailMessage(string action, EntityUid limb, ExternalOrganComponent ext)
    {
        var limbName = Name(limb);
        return action switch
        {
            "Incision" or "LaserIncision" or "ManagedIncision" => Loc.GetString("surgery-incision-fail", ("limb", limbName)),
            "Clamp" => Loc.GetString("surgery-clamp-fail", ("limb", limbName)),
            "Retract" => Loc.GetString("surgery-retract-fail", ("limb", limbName)),
            "Cauterize" => Loc.GetString("surgery-cauterize-fail", ("limb", limbName)),
            "SawBone" => Loc.GetString("surgery-saw-fail", ("limb", limbName)),
            "BoneGlue" => Loc.GetString("surgery-boneglue-fail", ("limb", limbName)),
            "BoneSet" => Loc.GetString("surgery-boneset-fail", ("limb", limbName)),
            "BoneFinish" => Loc.GetString("surgery-boneglue-fail", ("limb", limbName)),
            "Amputate" => Loc.GetString("surgery-amputate-fail", ("limb", limbName)),
            "OrganDetach" or "OrganRemove" or "OrganReplace" or "OrganAttach" or "OrganFix" => Loc.GetString("surgery-organ-fail", ("limb", limbName)),
            "RemoveEmbedded" => Loc.GetString("surgery-embedded-fail", ("limb", limbName)),
            _ => Loc.GetString("surgery-step-fail")
        };
    }

    private void ApplyFailDamage(string action, EntityUid limb, ExternalOrganComponent ext, EntityUid tool)
    {
        var damage = new DamageSpecifier();
        switch (action)
        {
            case "Incision" or "LaserIncision":
                damage.DamageDict.Add("Slash", FixedPoint2.New(10));
                break;
            case "ManagedIncision":
                damage.DamageDict.Add("Slash", FixedPoint2.New(20));
                damage.DamageDict.Add("Heat", FixedPoint2.New(15));
                break;
            case "Clamp":
                damage.DamageDict.Add("Slash", FixedPoint2.New(10));
                break;
            case "Retract":
                damage.DamageDict.Add("Slash", FixedPoint2.New(12));
                break;
            case "Cauterize":
                damage.DamageDict.Add("Heat", FixedPoint2.New(3));
                break;
            case "SawBone":
                damage.DamageDict.Add("Slash", FixedPoint2.New(15));
                break;
            case "BoneSet":
                damage.DamageDict.Add("Blunt", FixedPoint2.New(15));
                break;
            case "Amputate":
                damage.DamageDict.Add("Slash", FixedPoint2.New(30));
                break;
            default:
                damage.DamageDict.Add("Slash", FixedPoint2.New(5));
                break;
        }

        if (!damage.Empty)
            _damageable.TryChangeDamage(limb, damage, ignoreResistances: true);
    }

    private ProtoId<OrganCategoryPrototype> GetSurgeryTargetZone(EntityUid surgeon)
    {
        var targeting = EnsureComp<TargetingComponent>(surgeon);
        return BodyPartHelper.ToOrganCategory(targeting.ActivePart);
    }

    private bool IsPatientReady(EntityUid target)
    {
        if (TryComp<MobStateComponent>(target, out var mobState))
        {
            if (mobState.CurrentState != MobState.Alive)
                return true;

            return HasComp<SleepingComponent>(target)
                || HasComp<KnockedDownComponent>(target)
                || HasComp<StunnedComponent>(target);
        }

        return true;
    }

    private (EntityUid? limb, ExternalOrganComponent? ext) FindTargetLimb(BodyComponent body,
        ProtoId<OrganCategoryPrototype> targetZone)
    {
        if (body.Organs == null)
            return (null, null);

        EntityUid? fallback = null;
        ExternalOrganComponent? fallbackExt = null;

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (!TryComp<WoundableComponent>(organ, out _))
                continue;

            if (!TryComp<ExternalOrganComponent>(organ, out var external))
                continue;

            if (!TryComp<OrganComponent>(organ, out var organComp))
                continue;

            if (organComp.Category == targetZone)
                return (organ, external);

            if (fallback == null && organComp.Category == "Torso")
            {
                fallback = organ;
                fallbackExt = external;
            }
        }

        return (fallback, fallbackExt);
    }

    private bool IsValidSurgeryTarget(EntityUid limb, ExternalOrganComponent ext, string action)
    {
        // Block torso amputation
        if ((action == "BoneSaw" || action == "Amputate") && TryComp<OrganComponent>(limb, out var limbOrg)
            && limbOrg.Category == "Torso")
            return false;

        switch (action)
        {
            case "Incision":
                return ext.SurgeryStage == SurgeryStage.None && (ext.Status & OrganStatusFlags.CutAway) == 0;
            case "Clamp":
                return ext.SurgeryStage == SurgeryStage.Incised;
            case "Retract":
                return ext.SurgeryStage == SurgeryStage.Clamped;
            case "Cauterize":
                return ext.SurgeryStage != SurgeryStage.None;
            case "BoneSaw":
                return CanAmputate(ext) || ext.SurgeryStage == SurgeryStage.Retracted;
            case "SawBone":
                return ext.SurgeryStage == SurgeryStage.Retracted && !string.IsNullOrEmpty(ext.Encased);
            case "Amputate":
                return CanAmputate(ext);
            case "BoneGlue":
                return (ext.Status & OrganStatusFlags.Broken) != 0 && ext.BoneRepairStage == 0;
            case "BoneSet":
                return (ext.Status & OrganStatusFlags.Broken) != 0 && ext.BoneRepairStage == 1;
            case "BoneFinish":
                return (ext.Status & OrganStatusFlags.Broken) != 0 && ext.BoneRepairStage == 2;
            case "OrganDetach":
            case "OrganRemove":
            case "OrganReplace":
            case "OrganAttach":
            case "OrganFix":
            case "RemoveEmbedded":
                return ext.SurgeryStage == SurgeryStage.Retracted || ext.SurgeryStage == SurgeryStage.Encased;
            default:
                return (ext.Flags & LimbFlags.CanAmputate) != 0;
        }
    }

    public string? ExecuteStepAction(EntityUid tool, SurgeryToolComponent toolComp,
        EntityUid limb, ExternalOrganComponent ext, EntityUid target)
    {
        var action = toolComp.Action;

        switch (action)
        {
            case "Incision" when ext.SurgeryStage == SurgeryStage.None && (ext.Status & OrganStatusFlags.CutAway) == 0:
                ext.SurgeryStage = SurgeryStage.Incised;
                Dirty(limb, ext);
                return Loc.GetString("surgery-incision-end");

            case "LaserIncision" when CanIncise(ext):
                ext.SurgeryStage = SurgeryStage.Clamped;
                Dirty(limb, ext);
                return Loc.GetString("surgery-incision-laser-end");

            case "ManagedIncision" when CanIncise(ext):
                ext.SurgeryStage = SurgeryStage.Retracted;
                Dirty(limb, ext);
                return Loc.GetString("surgery-incision-managed-end");

            case "Clamp" when ext.SurgeryStage == SurgeryStage.Incised:
                ext.SurgeryStage = SurgeryStage.Clamped;
                Dirty(limb, ext);
                return Loc.GetString("surgery-clamp-end");

            case "Retract" when ext.SurgeryStage == SurgeryStage.Clamped:
                ext.SurgeryStage = SurgeryStage.Retracted;
                Dirty(limb, ext);
                return Loc.GetString("surgery-retract-end");

            case "Cauterize":
                ext.SurgeryStage = SurgeryStage.None;
                Dirty(limb, ext);
                return Loc.GetString("surgery-cauterize-end");

            case "SawBone" when ext.SurgeryStage == SurgeryStage.Retracted && !string.IsNullOrEmpty(ext.Encased):
                ext.SurgeryStage = SurgeryStage.Encased;
                Dirty(limb, ext);
                return Loc.GetString("surgery-saw-end", ("bone", ext.Encased));

            // Legacy BoneSaw support — test tools still use this action
            case "BoneSaw" when CanAmputate(ext) && ext.SurgeryStage == SurgeryStage.None:
            {
                var sawEv = new LimbAmputateEvent((limb, ext), DropLimbType.Edge);
                RaiseLocalEvent(limb, ref sawEv);
                return Loc.GetString("surgery-limb-amputated");
            }
            case "BoneSaw" when ext.SurgeryStage == SurgeryStage.Retracted:
                ext.SurgeryStage = SurgeryStage.Encased;
                Dirty(limb, ext);
                return Loc.GetString("surgery-bone-opened");

            case "BoneGlue" when (ext.Status & OrganStatusFlags.Broken) != 0 && ext.BoneRepairStage == 0:
                ext.BoneRepairStage = 1;
                Dirty(limb, ext);
                return Loc.GetString("surgery-boneglue-end");

            case "BoneGlue":
                return Loc.GetString("surgery-bone-not-broken");

            case "BoneSet" when (ext.Status & OrganStatusFlags.Broken) != 0 && ext.BoneRepairStage == 1:
                ext.BoneRepairStage = 2;
                Dirty(limb, ext);
                return Loc.GetString("surgery-boneset-end");

            case "BoneFinish" when (ext.Status & OrganStatusFlags.Broken) != 0 && ext.BoneRepairStage == 2:
                ext.BoneRepairStage = 0;
                ext.Status &= ~OrganStatusFlags.Broken;
                Dirty(limb, ext);
                return Loc.GetString("surgery-bonefinish-end");

            case "Amputate":
                var amputateEv = new LimbAmputateEvent((limb, ext), DropLimbType.Edge);
                RaiseLocalEvent(limb, ref amputateEv);
                return Loc.GetString("surgery-amputate-end");

            case "OrganDetach":
                return DetachOrgan(limb, ext);

            case "OrganRemove":
                return RemoveOrgan(limb, ext);

            case "OrganReplace":
                return ReplaceOrgan(tool, limb, ext);

            case "OrganAttach":
                return Loc.GetString("surgery-organ-attach-end");

            case "OrganFix":
                return FixInternalOrgan(limb, ext);

            case "RemoveEmbedded":
                return RemoveEmbeddedObject(limb);

            default:
                return Loc.GetString("surgery-step-not-valid", ("action", action));
        }
    }

    // Legacy test API — forwards to ExecuteStepAction
    public string? ExecuteStep(EntityUid tool, SurgeryToolComponent toolComp, EntityUid limb, ExternalOrganComponent ext, BodyComponent? body = null, EntityUid bodyEnt = default)
    {
        return ExecuteStepAction(tool, toolComp, limb, ext, bodyEnt != default ? bodyEnt : limb);
    }

    private static bool CanIncise(ExternalOrganComponent limb)
    {
        return limb.SurgeryStage == SurgeryStage.None
            && (limb.Status & OrganStatusFlags.CutAway) == 0;
    }

    private static bool CanAmputate(ExternalOrganComponent limb)
    {
        return (limb.Flags & LimbFlags.CanAmputate) != 0
            && limb.SurgeryStage == SurgeryStage.None;
    }

    private string? DetachOrgan(EntityUid limb, ExternalOrganComponent ext)
    {
        if (ext.SurgeryStage != SurgeryStage.Retracted && ext.SurgeryStage != SurgeryStage.Encased)
            return Loc.GetString("surgery-step-not-valid", ("action", "OrganDetach"));

        var bodyEnt = Comp<OrganComponent>(limb).Body;
        if (bodyEnt == null || !TryComp<BodyComponent>(bodyEnt.Value, out var body) || body.Organs == null)
            return Loc.GetString("surgery-no-organs");

        foreach (var innerOrgan in body.Organs.ContainedEntities)
        {
            if (innerOrgan == limb)
                continue;

            if (!HasComp<HeartConditionComponent>(innerOrgan)
                && !HasComp<LungConditionComponent>(innerOrgan)
                && !HasComp<BrainComponent>(innerOrgan))
                continue;

            EnsureComp<OrganDetachedComponent>(innerOrgan);
            return Loc.GetString("surgery-organ-detached", ("organ", Name(innerOrgan)));
        }

        return Loc.GetString("surgery-no-organs-in-limb");
    }

    private string? RemoveOrgan(EntityUid limb, ExternalOrganComponent ext)
    {
        if (ext.SurgeryStage != SurgeryStage.Retracted && ext.SurgeryStage != SurgeryStage.Encased)
            return Loc.GetString("surgery-step-not-valid", ("action", "OrganRemove"));

        var bodyEnt = Comp<OrganComponent>(limb).Body;
        if (bodyEnt == null || !TryComp<BodyComponent>(bodyEnt.Value, out var body) || body.Organs == null)
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
            if (body.Organs != null)
                _container.Remove(innerOrgan, body.Organs);
            var xform = Transform(innerOrgan);
            xform.Coordinates = Transform(bodyEnt.Value).Coordinates;

            return Loc.GetString("surgery-organ-removed", ("organ", Name(innerOrgan)));
        }

        return Loc.GetString("surgery-no-organs-in-limb");
    }

    private string? ReplaceOrgan(EntityUid heldOrgan, EntityUid limb, ExternalOrganComponent ext)
    {
        if (!TryComp<OrganComponent>(heldOrgan, out var organComp))
            return Loc.GetString("surgery-not-an-organ");

        if (organComp.Body != null)
            return Loc.GetString("surgery-organ-still-attached");

        // Get body from the limb being operated on
        if (!TryComp<OrganComponent>(limb, out var limbOrgan) || limbOrgan.Body == null)
            return Loc.GetString("surgery-no-organs");

        var bodyEnt = limbOrgan.Body.Value;
        if (!TryComp<BodyComponent>(bodyEnt, out var body) || body.Organs == null)
            return Loc.GetString("surgery-no-organs");

        _container.Insert(heldOrgan, body.Organs);

        return Loc.GetString("surgery-organ-replaced", ("organ", Name(heldOrgan)));
    }

    private string? FixInternalOrgan(EntityUid limb, ExternalOrganComponent ext)
    {
        var bodyEnt = Comp<OrganComponent>(limb).Body;
        if (bodyEnt == null || !TryComp<BodyComponent>(bodyEnt.Value, out var body) || body.Organs == null)
            return Loc.GetString("surgery-no-organs");

        foreach (var innerOrgan in body.Organs.ContainedEntities)
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

        return Loc.GetString("surgery-organ-repaired");
    }

    private string? RemoveEmbeddedObject(EntityUid limb)
    {
        if (!TryComp<WoundableComponent>(limb, out var wnd))
            return Loc.GetString("surgery-no-embedded");

        var removed = 0;
        foreach (var wUid in wnd.Wounds)
        {
            if (!TryComp<WoundEffectsComponent>(wUid, out var effects))
                continue;
            var embedded = effects.GetEffect("Embedded", _prototype);
            if (embedded == null || embedded.StringListParams.Count == 0)
                continue;

            embedded.StringListParams.RemoveAt(0);
            Dirty(wUid, effects);
            removed++;
        }

        if (removed > 0)
            return Loc.GetString("surgery-embedded-removed");

        return Loc.GetString("surgery-no-embedded");
    }
}

public sealed partial class SurgeryStepDoAfterEvent : DoAfterEvent
{
    [DataField]
    public EntityUid User;

    [DataField]
    public EntityUid Tool;

    [DataField]
    public ProtoId<SurgeryStepPrototype> StepId;

    [DataField]
    public float SuccessChance;

    [DataField]
    public EntityUid Limb;

    public override DoAfterEvent Clone() => this;
}

[RegisterComponent]
public sealed partial class SurgeryInProgressComponent : Component;

[RegisterComponent]
public sealed partial class OrganDetachedComponent : Component;
