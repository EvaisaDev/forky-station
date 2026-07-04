using System.Numerics;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organs;
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
    /// Dismember (remove) a limb from its body.
    /// </summary>
    public void DropLimb(Entity<ExternalOrganComponent> ent, DropLimbType type = DropLimbType.Edge)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (!TryComp<OrganComponent>(ent, out var organ) || organ.Body is not { } body)
            return;

        if (TryComp<ContainerManagerComponent>(body, out var containers))
        {
            if (_container.TryGetContainer(body, BodyComponent.ContainerID, out var container, containers))
            {
                _container.Remove(ent.Owner, container, force: true);
            }
        }

        var coords = new EntityCoordinates(body, Vector2.Zero);
        _transform.SetCoordinates(ent.Owner, coords);

        ent.Comp.Status |= OrganStatusFlags.CutAway;
        Dirty(ent);

        var ev = new LimbDismemberedEvent(body, ent, type);
        RaiseLocalEvent(body, ref ev);
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
