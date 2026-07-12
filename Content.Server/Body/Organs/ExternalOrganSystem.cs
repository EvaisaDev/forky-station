using System.Numerics;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organs;
using Content.Shared.Medical.Wounds;
using Content.Shared.FixedPoint;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server.Body.Organs;

/// <summary>
/// Handles limb-specific operations: fracture, dismemberment, artery/tendon severing.
/// </summary>
public sealed partial class ExternalOrganSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ExternalOrganComponent, LimbFractureCheckEvent>(OnFractureCheck);
        SubscribeLocalEvent<ExternalOrganComponent, LimbAmputateEvent>(OnAmputate);
    }

    private void OnFractureCheck(Entity<ExternalOrganComponent> ent, ref LimbFractureCheckEvent args)
    {
        if (args.Damage > 0)
            Fracture(ent);
    }

    private void OnAmputate(Entity<ExternalOrganComponent> ent, ref LimbAmputateEvent args)
    {
        DropLimb(ent, args.Type);
    }

    /// <summary>
    /// Fracture (break) a limb bone if it exceeds the MinBrokenDamage threshold.
    /// </summary>
    public void Fracture(Entity<ExternalOrganComponent> ent)
    {
        if ((ent.Comp.Status & OrganStatusFlags.Broken) != 0)
            return;

        ent.Comp.Status |= OrganStatusFlags.Broken;
        Dirty(ent);

        var ev = new LimbFracturedEvent(ent);
        RaiseLocalEvent(ent, ref ev);
    }

    /// <summary>
    /// Mend (heal) a fractured limb bone.
    /// </summary>
    public void MendFracture(Entity<ExternalOrganComponent> ent)
    {
        if ((ent.Comp.Status & OrganStatusFlags.Broken) == 0)
            return;

        ent.Comp.Status &= ~OrganStatusFlags.Broken;
        Dirty(ent);
    }

    /// <summary>
    /// Sever the artery in this limb, causing internal bleeding.
    /// </summary>
    public void SeverArtery(Entity<ExternalOrganComponent> ent)
    {
        if ((ent.Comp.Status & OrganStatusFlags.ArteryCut) != 0)
            return;

        ent.Comp.Status |= OrganStatusFlags.ArteryCut;
        Dirty(ent);
    }

    /// <summary>
    /// Mend a severed artery.
    /// </summary>
    public void MendArtery(Entity<ExternalOrganComponent> ent)
    {
        if ((ent.Comp.Status & OrganStatusFlags.ArteryCut) == 0)
            return;

        ent.Comp.Status &= ~OrganStatusFlags.ArteryCut;
        Dirty(ent);
    }

    /// <summary>
    /// Sever the tendon in this limb, disabling its function.
    /// </summary>
    public void SeverTendon(Entity<ExternalOrganComponent> ent)
    {
        if ((ent.Comp.Status & OrganStatusFlags.TendonCut) != 0)
            return;

        ent.Comp.Status |= OrganStatusFlags.TendonCut;
        Dirty(ent);
    }

    /// <summary>
    /// Mend a severed tendon.
    /// </summary>
    public void MendTendon(Entity<ExternalOrganComponent> ent)
    {
        if ((ent.Comp.Status & OrganStatusFlags.TendonCut) == 0)
            return;

        ent.Comp.Status &= ~OrganStatusFlags.TendonCut;
        Dirty(ent);
    }

    /// <summary>
    /// Maps a limb category to its dependent limb (e.g., ArmLeft -> HandLeft, LegLeft -> FootLeft).
    /// </summary>
    private static readonly Dictionary<string, string> LimbDependents = new()
    {
        { "ArmLeft", "HandLeft" },
        { "ArmRight", "HandRight" },
        { "LegLeft", "FootLeft" },
        { "LegRight", "FootRight" },
    };

    /// <summary>
    /// Dismember (remove) a limb from its body.
    /// Also removes dependent limbs (arm → hand, leg → foot).
    /// Spawns a stump in the body for reattachment surgery.
    /// </summary>
    public void DropLimb(Entity<ExternalOrganComponent> ent, DropLimbType type = DropLimbType.Edge)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (!TryComp<OrganComponent>(ent, out var organ) || organ.Body is not { } body)
            return;

        var category = organ.Category;

        // Remove dependent limb (hand when arm drops, foot when leg drops)
        if (category != null && LimbDependents.TryGetValue(category, out var dependentCategory))
        {
            DropChildLimb(body, dependentCategory, type);
        }

        if (TryComp<ContainerManagerComponent>(body, out var containers))
        {
            if (_container.TryGetContainer(body, BodyComponent.ContainerID, out var container, containers))
            {
                _container.Remove(ent.Owner, container, force: true);
            }
        }

        var dropCoords = Transform(body).Coordinates;
        _transform.SetCoordinates(ent.Owner, dropCoords);

        ent.Comp.Status |= OrganStatusFlags.CutAway;
        Dirty(ent);

        // Spawn a stump in the body so reattachment surgery can target it
        if (category != null)
        {
            SpawnStump(body, category);
        }

        var ev = new LimbDismemberedEvent(body, ent, type);
        RaiseLocalEvent(body, ref ev);
    }

    /// <summary>
    /// Spawns a stump organ in the body container at the given category slot.
    /// The stump has CutAway set so reattachment surgery can find and operate on it.
    /// </summary>
    private void SpawnStump(EntityUid body, string category)
    {
        if (!TryComp<ContainerManagerComponent>(body, out var containers))
            return;

        if (!_container.TryGetContainer(body, BodyComponent.ContainerID, out var container, containers))
            return;

        // Spawn the stump entity and set it up via EntityManager
        var stump = Spawn("OrganStump", Transform(body).Coordinates);

        // Set CutAway on the stump's external organ
        if (TryComp<ExternalOrganComponent>(stump, out var stumpExt))
        {
            stumpExt.Status |= OrganStatusFlags.CutAway;
            Dirty(stump, stumpExt);
        }

        // Initialize stump bleed timer
        if (TryComp<WoundEffectsComponent>(stump, out var stumpEffects))
        {
            foreach (var instance in stumpEffects.Effects)
            {
                if (instance.Id == "BleedingLarge" || instance.Id == "Bleeding")
                {
                    // Set bleed timer so the stump bleeds
                    if (!instance.FloatParams.ContainsKey("bleedTimer"))
                        instance.FloatParams["bleedTimer"] = 120;
                    if (!instance.FloatParams.ContainsKey("currentBleedAmount"))
                        instance.FloatParams["currentBleedAmount"] = 3.0f;
                    break;
                }
            }
            Dirty(stump, stumpEffects);
        }

        // Insert stump into the body — BodySystem.OnBodyEntInserted will set OrganComponent.Body
        _container.Insert(stump, container);
    }

    /// <summary>
    /// Find and remove a child limb (hand/foot) that depends on a parent limb (arm/leg).
    /// </summary>
    private void DropChildLimb(EntityUid body, string category, DropLimbType type)
    {
        if (!TryComp<BodyComponent>(body, out var bodyComp) || bodyComp.Organs == null)
            return;

        if (!TryComp<ContainerManagerComponent>(body, out var containers))
            return;

        if (!_container.TryGetContainer(body, BodyComponent.ContainerID, out var container, containers))
            return;

        foreach (var childOrgan in container.ContainedEntities)
        {
            if (TerminatingOrDeleted(childOrgan))
                continue;

            if (!TryComp<ExternalOrganComponent>(childOrgan, out var childExt))
                continue;

            if (!TryComp<OrganComponent>(childOrgan, out var childOrg))
                continue;

            if (childOrg.Category != category)
                continue;

            _container.Remove(childOrgan, container, force: true);
            var dropCoords = Transform(body).Coordinates;
            _transform.SetCoordinates(childOrgan, dropCoords);
            childExt.Status |= OrganStatusFlags.CutAway;
            Dirty(childOrgan, childExt);

            var ev = new LimbDismemberedEvent(body, (childOrgan, childExt), type);
            RaiseLocalEvent(body, ref ev);
            return;
        }
    }

    /// <summary>
    /// Check if a limb should be dismembered based on damage and damage type.
    /// </summary>
    public bool AttemptDismemberment(Entity<ExternalOrganComponent> ent, FixedPoint2 bruteDamage, FixedPoint2 burnDamage)
    {
        var totalDamage = ent.Comp.BruteDamage + bruteDamage + ent.Comp.BurnDamage + burnDamage;

        if (totalDamage < ent.Comp.MaxDamage)
            return false;

        if (bruteDamage > 0 && totalDamage >= ent.Comp.MaxDamage)
        {
            DropLimb(ent, DropLimbType.Edge);
            return true;
        }

        if (burnDamage > 0 && ent.Comp.BurnDamage + burnDamage >= ent.Comp.MaxDamage)
        {
            DropLimb(ent, DropLimbType.Burn);
            return true;
        }

        return false;
    }
}
