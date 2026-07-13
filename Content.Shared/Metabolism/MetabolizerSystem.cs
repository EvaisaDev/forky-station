using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Body.Events;
using Content.Shared.Body;
using Content.Shared.Damage;
using Content.Shared.EntityEffects.Effects.Damage;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityConditions;
using Content.Shared.EntityConditions.Conditions;
using Content.Shared.EntityConditions.Conditions.Body;
using Content.Shared.EntityEffects;
using Content.Shared.EntityEffects.Effects.Body;
using Content.Shared.EntityEffects.Effects.Solution;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Random.Helpers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.Metabolism;

/// <inheritdoc/>
public sealed partial class MetabolizerSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedEntityConditionsSystem _entityConditions = default!;
    [Dependency] private SharedEntityEffectsSystem _entityEffects = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;

    [Dependency] private EntityQuery<OrganComponent> _organQuery = default!;
    [Dependency] private EntityQuery<SolutionManagerComponent> _solutionQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MetabolizerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MetabolizerComponent, BodyRelayedEvent<ApplyMetabolicMultiplierEvent>>(OnApplyMetabolicMultiplier);
    }

    private void OnMapInit(Entity<MetabolizerComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdate = _gameTiming.CurTime + ent.Comp.AdjustedUpdateInterval;
        Dirty(ent);
    }

    private void OnApplyMetabolicMultiplier(Entity<MetabolizerComponent> ent, ref BodyRelayedEvent<ApplyMetabolicMultiplierEvent> args)
    {
        ent.Comp.UpdateIntervalMultiplier = args.Args.Multiplier;
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MetabolizerComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            // Only update as frequently as it should
            if (_gameTiming.CurTime < comp.NextUpdate)
                continue;

            comp.NextUpdate += comp.AdjustedUpdateInterval;
            TryMetabolize((uid, comp));
            Dirty(uid, comp);
        }
    }

    /// <summary>
    /// Updates the metabolic rate multiplier for a given entity,
    /// raising both <see cref="GetMetabolicMultiplierEvent"/> to determine what the multiplier is and <see cref="ApplyMetabolicMultiplierEvent"/> to update relevant components.
    /// </summary>
    /// <param name="uid"></param>
    public void UpdateMetabolicMultiplier(EntityUid uid)
    {
        var getEv = new GetMetabolicMultiplierEvent();
        RaiseLocalEvent(uid, ref getEv);

        var applyEv = new ApplyMetabolicMultiplierEvent(getEv.Multiplier);
        RaiseLocalEvent(uid, ref applyEv);
    }

    private bool LookupSolution(
        Entity<MetabolizerComponent, OrganComponent?, SolutionManagerComponent?> ent,
        MetabolismSolutionEntry solutionData,
        bool lookupTransfer,
        [NotNullWhen(true)] out Solution? solution,
        [NotNullWhen(true)] out Entity<SolutionComponent>? solutionEntity,
        [NotNullWhen(true)] out EntityUid? solutionOwner
    )
    {
        solution = null;
        solutionEntity = null;
        solutionOwner = null;

        var solutionName = lookupTransfer ? solutionData.TransferSolutionName : solutionData.SolutionName;

        if (solutionName is null)
            return false;

        if (lookupTransfer ? solutionData.TransferSolutionOnBody : solutionData.SolutionOnBody)
        {
            if (ent.Comp2?.Body is not { } body)
                return false;

            if (!_solutionContainerSystem.TryGetSolution(body, solutionName, out solutionEntity, out solution))
                return false;

            solutionOwner = body;
            return true;
        }

        if (!_solutionContainerSystem.TryGetSolution((ent, ent.Comp3), solutionName, out solutionEntity, out solution))
            return false;

        solutionOwner = ent;
        return true;
    }

    private void TryMetabolizeStage(Entity<MetabolizerComponent, OrganComponent?, SolutionManagerComponent?> ent, ProtoId<MetabolismStagePrototype> stage)
    {
        if (!ent.Comp1.Solutions.TryGetValue(stage, out var solutionData))
            return;

        if (!LookupSolution(ent, solutionData, false, out var solution, out var solutionEntity, out var solutionOwner))
            return;

        if (solution.Contents.Count == 0)
            return;

        LookupSolution(ent, solutionData, true, out var transferSolution, out var transferSolutionEntity, out _);

        var list = solution.Contents.ToList();

        var ev = new MetabolismExclusionEvent();
        RaiseLocalEvent(solutionOwner.Value, ref ev);

        var rand = SharedRandomExtensions.PredictedRandom(_gameTiming, GetNetEntity(ent), GetNetEntity(solutionOwner));
        rand.Shuffle(list);

        var isDead = _mobStateSystem.IsDead(solutionOwner.Value);

        // Stage name string for Bioavailability check
        var stageName = stage.Id;

        int reagents = 0;
        foreach (var (reagent, quantity) in list)
        {
            if (!_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype, out var proto))
                continue;

            if (ev.Reagents.Contains(reagent))
                continue;

            if (proto.Metabolisms is null || !proto.Metabolisms.Metabolisms.TryGetValue(stage, out var entry))
            {
                var mostToTransfer = FixedPoint2.Clamp(solutionData.TransferRate, 0, quantity);

                if (transferSolution is not null)
                {
                    solution.RemoveReagent(reagent, mostToTransfer);
                    transferSolution.AddReagent(reagent, mostToTransfer * solutionData.TransferEfficacy);
                }
                else
                {
                    solution.RemoveReagent(reagent, FixedPoint2.New(1));
                }

                continue;
            }

            // Use prototype-level MetabolismRate if set, otherwise use per-stage entry rate
            var rate = solutionData.MetabolizeAll
                ? quantity
                : proto.MetabolismRate != 0.2f
                    ? FixedPoint2.New(proto.MetabolismRate)
                    : entry.MetabolismRate;

            var mostToRemove = FixedPoint2.Clamp(rate, 0, quantity);

            // Baystation: Bioavailability scales oral dose in Digestion stage
            if (stageName == "Digestion" && proto.Bioavailability < 1.0f)
            {
                mostToRemove *= FixedPoint2.New(proto.Bioavailability);
            }

            if (reagents >= ent.Comp1.MaxReagentsProcessable)
                return;

            var scale = (float) mostToRemove;
            if (!solutionData.MetabolizeAll)
                scale /= (float) rate;

            if (isDead && !proto.WorksOnTheDead)
                continue;

            var actualEntity = ent.Comp2?.Body ?? solutionOwner.Value;

            foreach (var effect in entry.Effects)
            {
                if (scale < effect.MinScale)
                    continue;

                if (rand.NextFloat() >= effect.Probability)
                    continue;

                if (effect.Conditions != null && !CanMetabolizeEffect(actualEntity, ent, solutionEntity.Value, effect.Conditions))
                    continue;

                ApplyEffect(effect);
            }

            void ApplyEffect(EntityEffect effect)
            {
                switch (effect)
                {
                    case ModifyLungGas:
                        _entityEffects.ApplyEffect(ent, effect, scale);
                        break;
                    case AdjustReagent:
                        _entityEffects.ApplyEffect(solutionEntity.Value, effect, scale);
                        break;
                    default:
                        _entityEffects.ApplyEffect(actualEntity, effect, scale);
                        break;
                }
            }

            if (mostToRemove > FixedPoint2.Zero)
            {
                solution.RemoveReagent(reagent, mostToRemove);

                reagents += 1;

                if (transferSolution is not null && entry.Metabolites is not null)
                {
                    foreach (var (metabolite, ratio) in entry.Metabolites)
                    {
                        transferSolution.AddReagent(metabolite, mostToRemove * ratio);
                    }
                }

                // Baystation: ActiveMetabolite generation (Bloodstream stage)
                if (proto.ActiveMetabolite != null && stageName == "Bloodstream" && mostToRemove > 0)
                {
                    var metAmount = mostToRemove * FixedPoint2.New(proto.MetabolitePotency);
                    solution.AddReagent(proto.ActiveMetabolite, metAmount);
                }
            }
        }

        _solutionContainerSystem.UpdateChemicals(solutionEntity.Value);
        if (transferSolutionEntity is not null)
        {
            _solutionContainerSystem.UpdateChemicals(transferSolutionEntity.Value);
        }
    }

    private void TryMetabolize(Entity<MetabolizerComponent, OrganComponent?, SolutionManagerComponent?> ent)
    {
        _organQuery.Resolve(ent, ref ent.Comp2, logMissing: false);
        _solutionQuery.Resolve(ent, ref ent.Comp3, logMissing: false);

        foreach (var stage in ent.Comp1.Stages)
        {
            TryMetabolizeStage(ent, stage);
        }

        // Baystation: Overdose threshold check — scan bloodstream for reagents exceeding threshold
        CheckOverdose(ent);
    }

    private void CheckOverdose(Entity<MetabolizerComponent, OrganComponent?, SolutionManagerComponent?> ent)
    {
        if (!ent.Comp1.Solutions.TryGetValue("Bloodstream", out var solutionData))
            return;

        if (!LookupSolution(ent, solutionData, false, out var solution, out var solutionEntity, out var solutionOwner))
            return;

        if (solution.Contents.Count == 0)
            return;

        var actualEntity = ent.Comp2?.Body ?? solutionOwner.Value;

        foreach (var (reagent, quantity) in solution.Contents)
        {
            if (!_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype, out var proto))
                continue;

            if (proto.OverdoseThreshold <= 0)
                continue;

            if (quantity <= proto.OverdoseThreshold)
                continue;

            var excess = (float)(quantity - FixedPoint2.New(proto.OverdoseThreshold));
            var damageAmount = FixedPoint2.New(excess * 0.5f);
            if (damageAmount <= 0)
                continue;

            var damage = new DamageSpecifier
            {
                DamageDict = new() { { "Poison", damageAmount } }
            };
            var healthEffect = new HealthChange { Damage = damage };
            _entityEffects.ApplyEffect(actualEntity, healthEffect, 1f);
        }
    }

    /// <summary>
    /// Public API to check if a certain metabolism effect can be applied to an entity.
    /// TODO: With metabolism refactor make this logic smarter and unhardcode the old hardcoding entity effects used to have for metabolism!
    /// </summary>
    /// <param name="body">The body metabolizing the effects</param>
    /// <param name="organ">The organ doing the metabolizing</param>
    /// <param name="solution">The solution we are metabolizing from</param>
    /// <param name="conditions">The conditions that need to be met to metabolize</param>
    /// <returns>True if we can metabolize! False if we cannot!</returns>
    public bool CanMetabolizeEffect(EntityUid body, EntityUid organ, Entity<SolutionComponent> solution, EntityCondition[] conditions)
    {
        foreach (var condition in conditions)
        {
            switch (condition)
            {
                // Need specific handling of specific conditions since Metabolism is funny like that.
                // TODO: MetabolizerTypes should be handled well before this stage by metabolism itself.
                case MetabolizerTypeCondition:
                    if (_entityConditions.TryCondition(organ, condition))
                        continue;
                    break;
                case ReagentCondition:
                    if (_entityConditions.TryCondition(solution, condition))
                        continue;
                    break;
                default:
                    if (_entityConditions.TryCondition(body, condition))
                        continue;
                    break;
            }

            return false;
        }

        return true;
    }
}

