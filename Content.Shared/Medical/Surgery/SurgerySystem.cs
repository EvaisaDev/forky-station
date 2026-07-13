using Content.Shared._Medical.Targeting;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organs;
using Content.Shared.Body.Systems;
using Content.Shared.Chat;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Medical.Pain;
using Content.Shared.Medical.Wounds;
using Content.Shared.Mobs;
using Content.Shared.Movement.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
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
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SharedBloodstreamSystem _bloodstream = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SurgeryToolComponent, AfterInteractEvent>(OnToolAfterInteract);
        SubscribeLocalEvent<SurgeryToolComponent, SurgeryStepDoAfterEvent>(OnStepDoAfter);
    }

    public EntityUid? LastSurgeryTarget;
    public TimeSpan LastSurgeryTime;

    private void OnToolAfterInteract(Entity<SurgeryToolComponent> ent, ref AfterInteractEvent args)
    {
        if (!_net.IsServer)
            return;

        args.Handled = true;

        if (!args.CanReach || args.Target == null)
            return;

        var target = args.Target.Value;
        var (tool, toolComp) = ent;
        var curTime = _timing.CurTime;
        if (args.Target.Value == LastSurgeryTarget && curTime - LastSurgeryTime < TimeSpan.FromSeconds(1))
            return;
        LastSurgeryTarget = args.Target.Value;
        LastSurgeryTime = curTime;

        if (!TryComp<BodyComponent>(target, out var body) || body.Organs == null)
            return;

        var targetZone = GetSurgeryTargetZone(args.User);
        var (foundLimb, foundExt) = FindTargetLimb(body, targetZone);

        if (foundLimb == null)
        {
            _popup.PopupEntity(Loc.GetString("surgery-no-woundable-limb"), args.User, args.User);
            return;
        }

        var stepProto = FindBestStep(toolComp, foundLimb.Value, foundExt!);
        if (stepProto != null)
        {
            // Prevent starting surgery on a limb that's already being operated on
            if (HasComp<SurgeryInProgressComponent>(foundLimb.Value))
                return;

            var conscious = !IsPatientReady(target);
            BeginSurgeryStep(args.User, tool, toolComp, foundLimb.Value, foundExt!, target, body, stepProto, conscious);
        }
        else
        {
            var result = ExecuteStepAction(tool, toolComp, foundLimb.Value, foundExt!, target, args.User);
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

    private SurgeryStepPrototype? FindBestStep(SurgeryToolComponent toolComp, EntityUid limb, ExternalOrganComponent ext)
    {
        // Try the tool's explicit Steps list first
        if (toolComp.Steps.Count > 0)
        {
            foreach (var stepId in toolComp.Steps)
            {
                if (!_prototype.TryIndex(stepId, out var proto))
                    continue;

                if (IsValidSurgeryTarget(limb, ext, proto.Action))
                    return proto;
            }
        }

        // Fallback: match by action string (only if valid for current limb)
        var fallback = FindStepByAction(toolComp.Action);
        if (fallback != null && IsValidSurgeryTarget(limb, ext, fallback.Action))
            return fallback;

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
                EventUser = GetNetEntity(user),
                EventTool = GetNetEntity(tool),
                StepId = stepProto.ID,
                SuccessChance = successChance,
                EventLimb = GetNetEntity(limb)
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

        // Add infection risk from non-sterile tools (reduced by sterile operating table)
        if (stepProto.CanInfect && !toolComp.Sterile && TryComp<WoundableComponent>(limb, out var wnd))
        {
            var tableBonus = 1.0f;
            if (TryComp<BuckleComponent>(target, out var buckle) && buckle.Buckled
                && TryComp<OperativeTableComponent>(buckle.BuckledTo, out var opTable) && opTable.SterileEnvironment)
                tableBonus = 0.25f;

            var germToAdd = (int)(5 * tableBonus);

            foreach (var wUid in wnd.Wounds)
            {
                if (TryComp<WoundEffectsComponent>(wUid, out var effects))
                {
                    var germ = effects.GetEffect("Germ", _prototype);
                    if (germ != null)
                    {
                        germ.SetFloat("germLevel", germ.GetFloat("germLevel") + germToAdd);
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
            "BoneFinish" => Loc.GetString("surgery-bonefinish-start", ("tool", toolName), ("target", targetName)),
            "Amputate" => Loc.GetString("surgery-amputate-start", ("tool", toolName), ("target", targetName)),
            "OrganDetach" => Loc.GetString("surgery-organ-detach-start", ("tool", toolName), ("target", targetName)),
            "OrganRemove" => Loc.GetString("surgery-organ-remove-start", ("tool", toolName), ("target", targetName)),
            "OrganReplace" => Loc.GetString("surgery-organ-replace-start", ("tool", toolName), ("target", targetName)),
            "OrganAttach" => Loc.GetString("surgery-organ-attach-start", ("tool", toolName), ("target", targetName)),
            "OrganFix" => Loc.GetString("surgery-organ-fix-start", ("tool", toolName), ("target", targetName)),
            "RemoveEmbedded" => Loc.GetString("surgery-embedded-start", ("tool", toolName), ("target", targetName)),
            "TreatInternalBleeding" => Loc.GetString("surgery-treat-internal-bleeding-start", ("tool", toolName), ("target", targetName)),
            "CleanStump" => Loc.GetString("surgery-clean-stump-start", ("tool", toolName), ("target", targetName)),
            "TendonRepair" => Loc.GetString("surgery-tendon-repair-start", ("tool", toolName), ("target", targetName)),
            "MuscleRepair" => Loc.GetString("surgery-muscle-repair-start", ("tool", toolName), ("target", targetName)),
            "CloseReattachment" => Loc.GetString("surgery-close-reattachment-start", ("tool", toolName), ("target", targetName)),
            "MendFacial" => Loc.GetString("surgery-mend-facial-start", ("tool", toolName), ("target", targetName)),
            "RepairEye" => Loc.GetString("surgery-repair-eye-start", ("tool", toolName), ("target", targetName)),
            "RepairEar" => Loc.GetString("surgery-repair-ear-start", ("tool", toolName), ("target", targetName)),
            "ImplantItem" => Loc.GetString("surgery-implant-item-start", ("tool", toolName), ("target", targetName)),
            "RemoveImplanted" => Loc.GetString("surgery-remove-implant-start", ("tool", toolName), ("target", targetName)),
            "OpenHatch" => Loc.GetString("surgery-open-hatch-start", ("tool", toolName), ("target", targetName)),
            "CloseHatch" => Loc.GetString("surgery-close-hatch-start", ("tool", toolName), ("target", targetName)),
            "WeldBrute" => Loc.GetString("surgery-weld-brute-start", ("tool", toolName), ("target", targetName)),
            "ReplaceWires" => Loc.GetString("surgery-replace-wires-start", ("tool", toolName), ("target", targetName)),
            "DetachRobotic" => Loc.GetString("surgery-detach-robotic-start", ("tool", toolName), ("target", targetName)),
            "Autopsy" => Loc.GetString("surgery-autopsy-start", ("tool", toolName), ("target", targetName)),
            _ => null
        };
    }

    private void ApplySurgeryDamage(EntityUid body, EntityUid limb, ExternalOrganComponent ext, DamageSpecifier damage, bool isAmputation = false)
    {
        // Update limb damage values
        foreach (var (type, value) in damage.DamageDict)
        {
            if (value <= 0)
                continue;

            switch (type)
            {
                case "Blunt":
                case "Slash":
                case "Piercing":
                    ext.BruteDamage += value;
                    break;
                case "Heat":
                case "Cold":
                case "Shock":
                case "Caustic":
                    ext.BurnDamage += value;
                    break;
            }
        }
        Dirty(limb, ext);

        // For amputation, also damage the parent body part
        if (isAmputation && TryComp<OrganComponent>(limb, out var organ) && organ.Category != null)
        {
            ApplyToParentLimb(body, organ.Category, damage * 0.5f);
        }

        // Trigger bleeding on the body proportional to the damage
        var totalDamage = damage.GetTotal().Float();
        if (totalDamage > 0 && TryComp<BloodstreamComponent>(body, out var bloodstream))
        {
            _bloodstream.TryModifyBleedAmount((body, bloodstream), totalDamage * 0.5f);
        }
    }

    private void ApplyToParentLimb(EntityUid body, string limbCategory, DamageSpecifier damage)
    {
        if (!LimbParentCategories.TryGetValue(limbCategory, out var parentCategory))
            return;

        if (!TryComp<BodyComponent>(body, out var bodyComp) || bodyComp.Organs == null)
            return;

        foreach (var organ in bodyComp.Organs.ContainedEntities)
        {
            if (!TryComp<OrganComponent>(organ, out var org) || org.Category != parentCategory)
                continue;

            if (TryComp<ExternalOrganComponent>(organ, out var parentExt))
            {
                foreach (var (type, value) in damage.DamageDict)
                {
                    if (value <= 0)
                        continue;

                    switch (type)
                    {
                        case "Blunt":
                        case "Slash":
                        case "Piercing":
                            parentExt.BruteDamage += value;
                            break;
                        case "Heat":
                        case "Cold":
                        case "Shock":
                        case "Caustic":
                            parentExt.BurnDamage += value;
                            break;
                    }
                }
                Dirty(organ, parentExt);
            }
            return;
        }
    }

    private static readonly Dictionary<string, string> LimbParentCategories = new()
    {
        { "Head", "Torso" },
        { "ArmLeft", "Torso" },
        { "ArmRight", "Torso" },
        { "LegLeft", "Torso" },
        { "LegRight", "Torso" },
        { "HandLeft", "ArmLeft" },
        { "HandRight", "ArmRight" },
        { "FootLeft", "LegLeft" },
        { "FootRight", "LegRight" },
    };

    private float CalculateSuccessChance(SurgeryToolComponent toolComp, SurgeryStepPrototype stepProto,
        EntityUid limb, ExternalOrganComponent ext, EntityUid target, EntityUid user, bool conscious)
    {
        var chance = toolComp.Quality * 100f;

        // Self-surgery penalty
        if (user == target)
            chance -= 10;

        // Conscious patient penalty — awake patients flinch and move
        if (conscious)
            chance -= 15;

        // Surface quality bonus — check if patient is on an operating table
        if (stepProto.Delicate)
        {
            var tableQuality = GetTableQuality(target);
            if (tableQuality.HasValue)
                chance *= tableQuality.Value;
            else
                chance -= 10;
        }

        return Math.Clamp(chance, 0, 100);
    }

    private float? GetTableQuality(EntityUid target)
    {
        // Check if patient is buckled to an operating table
        if (TryComp<BuckleComponent>(target, out var buckle) && buckle.Buckled)
        {
            if (TryComp<OperativeTableComponent>(buckle.BuckledTo, out var table))
                return table.SurgeryQualityBonus;
        }
        return null;
    }

    private void OnStepDoAfter(Entity<SurgeryToolComponent> ent, ref SurgeryStepDoAfterEvent args)
    {
        if (!_net.IsServer)
            return;

        var limb = GetEntity(args.EventLimb);
        if (args.Handled || args.Cancelled)
        {
            if (limb != EntityUid.Invalid)
                RemComp<SurgeryInProgressComponent>(limb);
            return;
        }

        args.Handled = true;

        if (limb == EntityUid.Invalid || !TryComp<ExternalOrganComponent>(limb, out var ext))
            return;

        RemComp<SurgeryInProgressComponent>(limb);

        if (!_prototype.TryIndex(args.StepId, out var stepProto))
            return;

        var user = GetEntity(args.EventUser);
        var tool = GetEntity(args.EventTool);
        var target = user;

        if (args.Target is { } targ && targ != EntityUid.Invalid)
            target = targ;

        var success = _random.Prob(args.SuccessChance / 100f);

        if (success)
            EndSurgeryStep(user, tool, ent.Comp, limb, ext, target, stepProto);
        else
            FailSurgeryStep(user, tool, ent.Comp, limb, ext, target, stepProto);
    }

    private void EndSurgeryStep(EntityUid user, EntityUid tool, SurgeryToolComponent toolComp,
        EntityUid limb, ExternalOrganComponent ext, EntityUid target,
        SurgeryStepPrototype stepProto)
    {
        // Apply surgical trauma for amputation
        if (stepProto.Damage is { } stepDamage)
        {
            var isAmputation = stepProto.Action is "BoneSaw" or "Amputate" or "DetachRobotic";
            ApplySurgeryDamage(target, limb, ext, stepDamage, isAmputation);
        }

        var result = ExecuteStepAction(tool, toolComp, limb, ext, target, user, stepProto.Action);
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
            "Incision" => Loc.GetString("surgery-incision-fail", ("limb", limbName)),
            "LaserIncision" => Loc.GetString("surgery-incision-laser-fail", ("limb", limbName)),
            "ManagedIncision" => Loc.GetString("surgery-incision-managed-fail", ("limb", limbName)),
            "Clamp" => Loc.GetString("surgery-clamp-fail", ("limb", limbName)),
            "Retract" => Loc.GetString("surgery-retract-fail", ("limb", limbName)),
            "Cauterize" => Loc.GetString("surgery-cauterize-fail", ("limb", limbName)),
            "SawBone" => Loc.GetString("surgery-saw-fail", ("limb", limbName)),
            "BoneGlue" => Loc.GetString("surgery-boneglue-fail", ("limb", limbName)),
            "BoneSet" => Loc.GetString("surgery-boneset-fail", ("limb", limbName)),
            "BoneFinish" => Loc.GetString("surgery-bonefinish-fail", ("limb", limbName)),
            "Amputate" => Loc.GetString("surgery-amputate-fail", ("limb", limbName)),
            "OrganDetach" => Loc.GetString("surgery-organ-detach-fail", ("limb", limbName)),
            "OrganRemove" => Loc.GetString("surgery-organ-remove-fail", ("limb", limbName)),
            "OrganReplace" => Loc.GetString("surgery-organ-replace-fail", ("limb", limbName)),
            "OrganAttach" => Loc.GetString("surgery-organ-attach-fail", ("limb", limbName)),
            "OrganFix" => Loc.GetString("surgery-organ-fix-fail", ("limb", limbName)),
            "RemoveEmbedded" => Loc.GetString("surgery-embedded-fail", ("limb", limbName)),
            "TreatInternalBleeding" => Loc.GetString("surgery-treat-internal-bleeding-fail", ("limb", limbName)),
            "CleanStump" => Loc.GetString("surgery-clean-stump-fail", ("limb", limbName)),
            "TendonRepair" => Loc.GetString("surgery-tendon-repair-fail", ("limb", limbName)),
            "MuscleRepair" => Loc.GetString("surgery-muscle-repair-fail", ("limb", limbName)),
            "CloseReattachment" => Loc.GetString("surgery-close-reattachment-fail", ("limb", limbName)),
            "MendFacial" => Loc.GetString("surgery-mend-facial-fail", ("limb", limbName)),
            "RepairEye" => Loc.GetString("surgery-repair-eye-fail", ("limb", limbName)),
            "RepairEar" => Loc.GetString("surgery-repair-ear-fail", ("limb", limbName)),
            "ImplantItem" => Loc.GetString("surgery-implant-item-fail", ("limb", limbName)),
            "RemoveImplanted" => Loc.GetString("surgery-remove-implant-fail", ("limb", limbName)),
            "OpenHatch" => Loc.GetString("surgery-open-hatch-fail", ("limb", limbName)),
            "CloseHatch" => Loc.GetString("surgery-close-hatch-fail", ("limb", limbName)),
            "WeldBrute" => Loc.GetString("surgery-weld-brute-fail", ("limb", limbName)),
            "ReplaceWires" => Loc.GetString("surgery-replace-wires-fail", ("limb", limbName)),
            "DetachRobotic" => Loc.GetString("surgery-detach-robotic-fail", ("limb", limbName)),
            "Autopsy" => Loc.GetString("surgery-autopsy-fail", ("limb", limbName)),
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
        EntityUid? stumpLimb = null;
        ExternalOrganComponent? stumpExt = null;

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

            // Track stumps (CutAway limbs with no category) for reattachment targeting
            if ((external.Status & OrganStatusFlags.CutAway) != 0)
            {
                stumpLimb = organ;
                stumpExt = external;
            }
            else if (fallback == null && organComp.Category == "Torso")
            {
                fallback = organ;
                fallbackExt = external;
            }
        }

        // Prefer stumps over torso fallback
        if (stumpLimb != null)
            return (stumpLimb, stumpExt);

        return (fallback, fallbackExt);
    }

    private bool IsValidSurgeryTarget(EntityUid limb, ExternalOrganComponent ext, string action)
    {
        if ((action == "BoneSaw" || action == "Amputate") && TryComp<OrganComponent>(limb, out var limbOrg)
            && limbOrg.Category == "Torso")
            return false;

        switch (action)
        {
            case "Incision":
                return ext.SurgeryStage == SurgeryStage.None && (ext.Status & OrganStatusFlags.CutAway) == 0;
            case "Clamp":
                return ext.SurgeryStage == SurgeryStage.Incised && (ext.Status & OrganStatusFlags.CutAway) == 0;
            case "Retract":
                return ext.SurgeryStage == SurgeryStage.Clamped && (ext.Status & OrganStatusFlags.CutAway) == 0;
            case "Cauterize":
                return ext.SurgeryStage != SurgeryStage.None && (ext.Status & OrganStatusFlags.CutAway) == 0;
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
            case "TreatInternalBleeding":
                return ext.SurgeryStage == SurgeryStage.Retracted || ext.SurgeryStage == SurgeryStage.Encased;
            case "CleanStump":
                return (ext.Status & OrganStatusFlags.CutAway) != 0 && ext.SurgeryStage == SurgeryStage.None;
            case "TendonRepair":
                return (ext.Status & OrganStatusFlags.CutAway) != 0 && ext.SurgeryStage == SurgeryStage.Incised;
            case "MuscleRepair":
                return (ext.Status & OrganStatusFlags.CutAway) != 0 && ext.SurgeryStage == SurgeryStage.Clamped;
            case "CloseReattachment":
                return (ext.Status & OrganStatusFlags.CutAway) != 0;
            case "MendFacial":
                return ext.Disfigured && ext.SurgeryStage == SurgeryStage.Retracted;
            case "RepairEye":
            case "RepairEar":
                return ext.SurgeryStage == SurgeryStage.Retracted;
            case "ImplantItem":
            case "RemoveImplanted":
                return ext.SurgeryStage == SurgeryStage.Retracted || ext.SurgeryStage == SurgeryStage.Encased;
            case "OpenHatch":
                return (ext.Status & OrganStatusFlags.Robotic) != 0 && ext.SurgeryStage == SurgeryStage.None;
            case "CloseHatch":
                return (ext.Status & OrganStatusFlags.Robotic) != 0 && ext.SurgeryStage == SurgeryStage.Incised;
            case "WeldBrute":
                return (ext.Status & OrganStatusFlags.Robotic) != 0 && ext.BruteDamage > 0 && ext.SurgeryStage == SurgeryStage.Incised;
            case "ReplaceWires":
                return (ext.Status & OrganStatusFlags.Robotic) != 0 && ext.BurnDamage > 0 && ext.SurgeryStage == SurgeryStage.Incised;
            case "DetachRobotic":
                return (ext.Status & OrganStatusFlags.Robotic) != 0 && ext.SurgeryStage == SurgeryStage.None;
            case "Autopsy":
                return true;
            default:
                return (ext.Flags & LimbFlags.CanAmputate) != 0;
        }
    }

    public string? ExecuteStepAction(EntityUid tool, SurgeryToolComponent toolComp,
        EntityUid limb, ExternalOrganComponent ext, EntityUid target, EntityUid user = default,
        string? overrideAction = null)
    {
        var action = overrideAction ?? toolComp.Action;

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

            case "Clamp" when ext.SurgeryStage == SurgeryStage.Incised && (ext.Status & OrganStatusFlags.CutAway) == 0:
                ext.SurgeryStage = SurgeryStage.Clamped;
                Dirty(limb, ext);
                return Loc.GetString("surgery-clamp-end");

            case "Retract" when ext.SurgeryStage == SurgeryStage.Clamped && (ext.Status & OrganStatusFlags.CutAway) == 0:
                ext.SurgeryStage = SurgeryStage.Retracted;
                Dirty(limb, ext);
                return Loc.GetString("surgery-retract-end");

            case "Cauterize" when (ext.Status & OrganStatusFlags.CutAway) == 0:
                ext.SurgeryStage = SurgeryStage.None;
                // Also seals any severed artery (internal bleeding)
                if ((ext.Status & OrganStatusFlags.ArteryCut) != 0)
                {
                    ext.Status &= ~OrganStatusFlags.ArteryCut;
                    Dirty(limb, ext);
                    return Loc.GetString("surgery-cauterize-artery-end");
                }
                Dirty(limb, ext);
                return Loc.GetString("surgery-cauterize-end");

            case "SawBone" when ext.SurgeryStage == SurgeryStage.Retracted && !string.IsNullOrEmpty(ext.Encased):
                ext.SurgeryStage = SurgeryStage.Encased;
                Dirty(limb, ext);
                return Loc.GetString("surgery-saw-end", ("bone", ext.Encased));

            // Legacy BoneSaw support — test tools still use this action
            case "BoneSaw" when CanAmputate(ext) && ext.SurgeryStage == SurgeryStage.None
                && TryComp<OrganComponent>(limb, out var bsOrg) && bsOrg.Category != "Torso":
            {
                var sawEv = new LimbAmputateEvent((limb, ext), DropLimbType.Edge);
                RaiseLocalEvent(limb, ref sawEv);
                _movementSpeed.RefreshMovementSpeedModifiers(target);
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

            case "Amputate" when TryComp<OrganComponent>(limb, out var ampOrg) && ampOrg.Category != "Torso":
                var amputateEv = new LimbAmputateEvent((limb, ext), DropLimbType.Edge);
                RaiseLocalEvent(limb, ref amputateEv);
                _movementSpeed.RefreshMovementSpeedModifiers(target);
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

            case "TreatInternalBleeding" when (ext.Status & OrganStatusFlags.ArteryCut) != 0:
                ext.Status &= ~OrganStatusFlags.ArteryCut;
                Dirty(limb, ext);
                return Loc.GetString("surgery-treat-internal-bleeding-end");

            case "CleanStump" when (ext.Status & OrganStatusFlags.CutAway) != 0 && ext.SurgeryStage == SurgeryStage.None:
                ext.SurgeryStage = SurgeryStage.Incised;
                Dirty(limb, ext);
                return Loc.GetString("surgery-clean-stump-end");

            case "TendonRepair" when (ext.Status & OrganStatusFlags.CutAway) != 0 && ext.SurgeryStage == SurgeryStage.Incised:
                ext.Status &= ~OrganStatusFlags.TendonCut;
                ext.SurgeryStage = SurgeryStage.Clamped;
                Dirty(limb, ext);
                return Loc.GetString("surgery-tendon-repair-end");

            case "MuscleRepair" when (ext.Status & OrganStatusFlags.CutAway) != 0 && ext.SurgeryStage == SurgeryStage.Clamped:
                ext.SurgeryStage = SurgeryStage.Retracted;
                Dirty(limb, ext);
                return Loc.GetString("surgery-muscle-repair-end");

            case "CloseReattachment" when (ext.Status & OrganStatusFlags.CutAway) != 0:
                return ReattachLimb(limb, ext, target);

            case "MendFacial" when ext.Disfigured && ext.SurgeryStage == SurgeryStage.Retracted:
                ext.Disfigured = false;
                Dirty(limb, ext);
                return Loc.GetString("surgery-mend-facial-end");

            case "RepairEye" when ext.SurgeryStage == SurgeryStage.Retracted:
                return Loc.GetString("surgery-repair-eye-end");

            case "RepairEar" when ext.SurgeryStage == SurgeryStage.Retracted:
                return Loc.GetString("surgery-repair-ear-end");

            case "ImplantItem" when ext.SurgeryStage == SurgeryStage.Retracted || ext.SurgeryStage == SurgeryStage.Encased:
                return ImplantItem(limb, ext, tool, target, user);

            case "RemoveImplanted" when ext.SurgeryStage == SurgeryStage.Retracted || ext.SurgeryStage == SurgeryStage.Encased:
                return RemoveImplanted(limb, ext, target);

            case "OpenHatch" when (ext.Status & OrganStatusFlags.Robotic) != 0 && ext.SurgeryStage == SurgeryStage.None:
                ext.SurgeryStage = SurgeryStage.Incised;
                Dirty(limb, ext);
                return Loc.GetString("surgery-open-hatch-end");

            case "CloseHatch" when (ext.Status & OrganStatusFlags.Robotic) != 0 && ext.SurgeryStage == SurgeryStage.Incised:
                ext.SurgeryStage = SurgeryStage.None;
                Dirty(limb, ext);
                return Loc.GetString("surgery-close-hatch-end");

            case "WeldBrute" when (ext.Status & OrganStatusFlags.Robotic) != 0 && ext.BruteDamage > 0 && ext.SurgeryStage == SurgeryStage.Incised:
                ext.BruteDamage = FixedPoint2.Zero;
                Dirty(limb, ext);
                return Loc.GetString("surgery-weld-brute-end");

            case "ReplaceWires" when (ext.Status & OrganStatusFlags.Robotic) != 0 && ext.BurnDamage > 0 && ext.SurgeryStage == SurgeryStage.Incised:
                ext.BurnDamage = FixedPoint2.Zero;
                Dirty(limb, ext);
                return Loc.GetString("surgery-replace-wires-end");

            case "DetachRobotic" when (ext.Status & OrganStatusFlags.Robotic) != 0 && ext.SurgeryStage == SurgeryStage.None:
            {
                var detachEv = new LimbAmputateEvent((limb, ext), DropLimbType.Edge);
                RaiseLocalEvent(limb, ref detachEv);
                _movementSpeed.RefreshMovementSpeedModifiers(target);
                return Loc.GetString("surgery-detach-robotic-end");
            }

            case "Autopsy":
                return PerformAutopsy(limb, ext, target);

            default:
                return Loc.GetString("surgery-step-not-valid", ("action", action));
        }
    }

    // Legacy test API — forwards to ExecuteStepAction
    public string? ExecuteStep(EntityUid tool, SurgeryToolComponent toolComp, EntityUid limb, ExternalOrganComponent ext, BodyComponent? body = null, EntityUid bodyEnt = default)
    {
        return ExecuteStepAction(tool, toolComp, limb, ext, bodyEnt != default ? bodyEnt : limb, default);
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

    private string? ReattachLimb(EntityUid limb, ExternalOrganComponent ext, EntityUid target)
    {
        EntityUid? severedLimb = null;

        // Find any severed limb nearby (stump has no category, so we match by CutAway)
        var coords = Transform(target).Coordinates;
        foreach (var nearby in _lookup.GetEntitiesInRange(coords, 2.0f))
        {
            if (nearby == limb || TerminatingOrDeleted(nearby))
                continue;

            if (!TryComp<ExternalOrganComponent>(nearby, out var nearExt))
                continue;

            if (!TryComp<OrganComponent>(nearby, out _))
                continue;

            if ((nearExt.Status & OrganStatusFlags.CutAway) == 0)
                continue;

            severedLimb = nearby;
            break;
        }

        if (severedLimb == null)
            return Loc.GetString("surgery-reattach-no-limb");

        // Insert the severed limb back into the body
        if (!TryComp<BodyComponent>(target, out var bodyComp) || bodyComp.Organs == null)
            return Loc.GetString("surgery-no-woundable-limb");

        _container.Insert(severedLimb.Value, bodyComp.Organs);

        // Remove the stump from the body (it's no longer needed)
        _container.Remove(limb, bodyComp.Organs);
        QueueDel(limb);

        return Loc.GetString("surgery-close-reattachment-end");
    }

    private string? ImplantItem(EntityUid limb, ExternalOrganComponent ext, EntityUid tool, EntityUid target, EntityUid user)
    {
        if (!TryComp<BodyComponent>(target, out var bodyComp) || bodyComp.Organs == null)
            return Loc.GetString("surgery-no-woundable-limb");

        EntityUid? heldItem = null;
        if (TryComp<HandsComponent>(user, out var hands))
        {
            foreach (var held in _handsSystem.EnumerateHeld((user, hands)))
            {
                if (held != tool)
                {
                    heldItem = held;
                    break;
                }
            }
        }

        if (heldItem == null || !HasComp<ItemComponent>(heldItem.Value))
            return Loc.GetString("surgery-implant-no-item");

        _container.Insert(heldItem.Value, bodyComp.Organs);
        return Loc.GetString("surgery-implant-item-end");
    }

    private string? RemoveImplanted(EntityUid limb, ExternalOrganComponent ext, EntityUid target)
    {
        if (!TryComp<BodyComponent>(target, out var bodyComp) || bodyComp.Organs == null)
            return Loc.GetString("surgery-no-woundable-limb");

        foreach (var organ in bodyComp.Organs.ContainedEntities)
        {
            if (TryComp<ItemComponent>(organ, out _))
            {
                _container.Remove(organ, bodyComp.Organs);
                var dropCoords = Transform(target).Coordinates;
                Transform(organ).Coordinates = dropCoords;
                return Loc.GetString("surgery-remove-implant-end");
            }
        }

        return Loc.GetString("surgery-remove-implant-none");
    }

    private string? PerformAutopsy(EntityUid limb, ExternalOrganComponent ext, EntityUid target)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Autopsy findings for " + Name(target) + ":");

        if (TryComp<MobStateComponent>(target, out var mobState))
            sb.AppendLine("State: " + mobState.CurrentState);

        sb.AppendLine("Limb: " + Name(limb));
        sb.AppendLine("Brute damage: " + ext.BruteDamage);
        sb.AppendLine("Burn damage: " + ext.BurnDamage);
        sb.AppendLine("Flags: " + ext.Status);

        if (TryComp<DamageableComponent>(target, out var dmgComp))
        {
            var dmgPerGroup = _damageable.GetDamagePerGroup((target, dmgComp));
            foreach (var (group, dmg) in dmgPerGroup)
            {
                if (dmg > 0)
                    sb.AppendLine(group + ": " + dmg);
            }
        }

        _chat.SendAdminAlert(target, sb.ToString());
        return Loc.GetString("surgery-autopsy-end");
    }
}

[Serializable, NetSerializable]
public sealed partial class SurgeryStepDoAfterEvent : DoAfterEvent
{
    [DataField]
    public NetEntity EventUser;

    [DataField]
    public NetEntity EventTool;

    [DataField]
    public ProtoId<SurgeryStepPrototype> StepId;

    [DataField]
    public float SuccessChance;

    [DataField]
    public NetEntity EventLimb;

    public override DoAfterEvent Clone() => this;
}

[RegisterComponent]
public sealed partial class SurgeryInProgressComponent : Component;

[RegisterComponent]
public sealed partial class OrganDetachedComponent : Component;
