using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Shared.Body.Systems;

public sealed partial class BrainSystem
{
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    /// <summary>
    /// Apply brain damage, reducing brain integrity.
    /// </summary>
    public void TakeBrainDamage(Entity<BrainComponent> ent, FixedPoint2 amount)
    {
        if (amount <= 0 || TerminatingOrDeleted(ent))
            return;

        ent.Comp.Integrity = FixedPoint2.Max(ent.Comp.Integrity - amount, ent.Comp.MinDeadIntegrity);
        Dirty(ent);

        if (ent.Comp.Integrity <= ent.Comp.MinDeadIntegrity)
        {
            ent.Comp.HasBeenDead = true;
            Dirty(ent);
            OnBrainDeath(ent);
        }
    }

    /// <summary>
    /// Heal brain damage, restoring integrity.
    /// </summary>
    public void HealBrainDamage(Entity<BrainComponent> ent, FixedPoint2 amount)
    {
        if (amount <= 0 || TerminatingOrDeleted(ent))
            return;

        ent.Comp.Integrity = FixedPoint2.Min(ent.Comp.Integrity + amount, ent.Comp.MaxIntegrity);
        Dirty(ent);
    }

    /// <summary>
    /// Called when brain integrity reaches 0. Sets owner mob to Dead state.
    /// </summary>
    private void OnBrainDeath(Entity<BrainComponent> ent)
    {
        var body = FindBodyOwningBrain(ent);
        if (body == null)
            return;

        var target = body.Value;

        if (!_mobState.IsDead(target))
        {
            // Directly change mob state to dead, bypassing threshold system
            if (TryComp<MobStateComponent>(target, out var mobState))
            {
                _mobState.ChangeMobState(target, MobState.Dead, mobState);
            }
        }

        var ev = new BrainDeathEvent(target, ent);
        RaiseLocalEvent(target, ref ev);
    }

    /// <summary>
    /// Finds the body that contains this brain.
    /// </summary>
    private EntityUid? FindBodyOwningBrain(Entity<BrainComponent> ent)
    {
        if (TryComp<OrganComponent>(ent, out var organ) && organ.Body is { } body)
            return body;

        return null;
    }

    /// <summary>
    /// Gets the current brain integrity percentage (0.0 - 1.0).
    /// </summary>
    public float GetBrainIntegrityPercent(Entity<BrainComponent> ent)
    {
        if (ent.Comp.MaxIntegrity == 0)
            return 0f;

        return (float)(ent.Comp.Integrity / ent.Comp.MaxIntegrity).Float();
    }
}


