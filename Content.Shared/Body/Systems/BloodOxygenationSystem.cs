using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.Body.Systems;

/// <summary>
/// Computes blood oxygenation and pulse rate each tick.
/// Oxygenation is derived from blood volume × lung efficiency × heart efficiency + dexalin (for now, will replace with other airloss chems).
/// Low O2 causes brain damage. High pulse damages the heart and can trigger cardiac arrest.
/// </summary>
public sealed partial class BloodOxygenationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private BrainSystem _brain = default!;

    public const float O2_THRESHOLD_BRAIN_DAMAGE = 0.85f;
    public const float O2_THRESHOLD_LETHAL = 0.30f;
    public const float PULSE_THRESHOLD_HEART_DAMAGE = 150f;
    public const float PULSE_THRESHOLD_ARREST = 250f;
    public const float DEXALIN_BONUS = 0.50f;
    public const float DEXALIN_PLUS_BONUS = 0.80f;

    /// <summary>
    ///     How often the oxygenation system updates (every 2 seconds = 1 tick at default 20TPS).
    /// </summary>
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);

    private const string Dexalin = "Dexalin";
    private const string DexalinPlus = "DexalinPlus";

    private HashSet<EntityUid> _toProcess = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BloodOxygenationComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BloodOxygenationComponent, EntityTerminatingEvent>(OnTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<BloodOxygenationComponent>();
        while (query.MoveNext(out var uid, out var oxygenation))
        {
            ProcessOxygenation(uid, oxygenation);
        }
    }

    private void OnMapInit(Entity<BloodOxygenationComponent> ent, ref MapInitEvent args)
    {
        // Initialize with healthy values
        ent.Comp.Oxygenation = 1.0f;
        ent.Comp.PulseRate = ent.Comp.BasePulse;
        ent.Comp.CardiacArrest = false;
    }

    private void OnTerminating(Entity<BloodOxygenationComponent> ent, ref EntityTerminatingEvent args)
    {
    }

    private void ProcessOxygenation(EntityUid uid, BloodOxygenationComponent oxygenation)
    {
        if (_mobState.IsDead(uid))
            return;

        // If in cardiac arrest, O2 rapidly drops
        if (oxygenation.CardiacArrest)
        {
            oxygenation.Oxygenation = Math.Max(0, oxygenation.Oxygenation - 0.15f);
            oxygenation.PulseRate = 0;
            ApplyOxygenEffects(uid, oxygenation);
            return;
        }
        // Calculations to get oxygenation and its effects in order from top to bottom

        // Get blood volume ratio
        var bloodVolume = GetBloodVolumeRatio(uid);

        // Get lung efficiency
        var lungEff = GetLungEfficiency(uid);

        // Get heart efficiency
        var heartEff = GetHeartEfficiency(uid);

        // Check for dexalin
        var dexalinBonus = GetDexalinBonus(uid);

        // Compute oxygenation
        oxygenation.Oxygenation = bloodVolume * lungEff * heartEff + dexalinBonus;
        oxygenation.Oxygenation = Math.Clamp(oxygenation.Oxygenation, 0, 1);

        // Compute pulse rate (inversely proportional to O2)
        var o2Safe = Math.Max(oxygenation.Oxygenation, 0.01f);
        oxygenation.PulseRate = Math.Clamp(
            oxygenation.BasePulse * (1.0f / o2Safe),
            0, // minimum 0 (arrest)
            300 // maximum 300
        );

        // Apply effects
        ApplyOxygenEffects(uid, oxygenation);

        // Check for cardiac arrest from high pulse
        if (oxygenation.PulseRate >= PULSE_THRESHOLD_ARREST)
        {
            SetCardiacArrest(uid, oxygenation, true);
        }
    }

    private float GetBloodVolumeRatio(EntityUid uid)
    {
        if (!TryComp<BloodstreamComponent>(uid, out var bloodstream))
            return 1.0f;

        // BloodstreamComponent doesn't expose total volume easily
        // so we calculate it from the solution
        if (!_solution.ResolveSolution(uid, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var bloodSolution))
            return 0.5f; // default to half if we can't resolve

        var refVol = bloodstream.BloodReferenceSolution.Volume;
        if (refVol == 0)
            return 1.0f;

        var currentVol = bloodSolution.Volume;
        return Math.Clamp((float)(currentVol / refVol), 0, 1);
    }

    private float GetLungEfficiency(EntityUid uid)
    {
        // Check the body's lung organs for condition
        if (!TryComp<BodyComponent>(uid, out var body) || body.Organs == null)
            return 1.0f;

        var totalEff = 0f;
        var count = 0;

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (TryComp<LungConditionComponent>(organ, out var lung))
            {
                totalEff += lung.Efficiency;
                count++;
            }
        }

        return count > 0 ? totalEff / count : 1.0f;
    }

    private float GetHeartEfficiency(EntityUid uid)
    {
        if (!TryComp<BodyComponent>(uid, out var body) || body.Organs == null)
            return 1.0f;

        var totalEff = 0f;
        var count = 0;

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (TryComp<HeartConditionComponent>(organ, out var heart))
            {
                if (!heart.Beating)
                    return 0f; // Heart not beating = zero output

                totalEff += heart.Efficiency;
                count++;
            }
        }

        return count > 0 ? totalEff / count : 1.0f;
    }

    private float GetDexalinBonus(EntityUid uid)
    {
        if (!TryComp<BloodstreamComponent>(uid, out var bloodstream))
            return 0f;

        if (!_solution.ResolveSolution(uid, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var bloodSolution))
            return 0f;

        var dexalinAmount = bloodSolution.GetTotalPrototypeQuantity(DexalinPlus);
        if (dexalinAmount > 0)
            return DEXALIN_PLUS_BONUS;

        dexalinAmount = bloodSolution.GetTotalPrototypeQuantity(Dexalin);
        if (dexalinAmount > 0)
            return DEXALIN_BONUS;

        return 0f;
    }

    private void ApplyOxygenEffects(EntityUid uid, BloodOxygenationComponent oxygenation)
    {
        if (oxygenation.Oxygenation >= O2_THRESHOLD_BRAIN_DAMAGE)
        {
            // Healthy - heal any accumulated brain damage slowly
            if (oxygenation.AccumulatedBrainDamage > 0)
            {
                var heal = FixedPoint2.New(Math.Min(oxygenation.AccumulatedBrainDamage, 0.5f));
                oxygenation.AccumulatedBrainDamage -= (float)heal;
                TryApplyBrainDamage(uid, -heal);
            }
            return;
        }

        // Below threshold - deal brain damage proportional to deficit
        var deficit = O2_THRESHOLD_BRAIN_DAMAGE - oxygenation.Oxygenation;
        // Scale: at 85% O2 = 0 brain damage/tick, at 0% O2 = ~8.5 brain damage/tick
        var damageAmount = FixedPoint2.New(deficit * 10f);

        oxygenation.AccumulatedBrainDamage += (float)damageAmount;
        TryApplyBrainDamage(uid, damageAmount);

        // Below lethal threshold - rapid brain death
        if (oxygenation.Oxygenation < O2_THRESHOLD_LETHAL)
        {
            var lethalDamage = FixedPoint2.New(5f);
            oxygenation.AccumulatedBrainDamage += (float)lethalDamage;
            TryApplyBrainDamage(uid, lethalDamage);
        }
    }

    private void TryApplyBrainDamage(EntityUid uid, FixedPoint2 amount)
    {
        if (!TryComp<BodyComponent>(uid, out var body) || body.Organs == null)
            return;

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (TryComp<BrainComponent>(organ, out var brain))
            {
                if (amount > 0)
                    _brain.TakeBrainDamage((organ, brain), amount);
                else
                    _brain.HealBrainDamage((organ, brain), -amount);
                return;
            }
        }
    }

    /// <summary>
    /// Set cardiac arrest state on an entity.
    /// </summary>
    public void SetCardiacArrest(EntityUid uid, BloodOxygenationComponent? oxygenation = null, bool arrest = true)
    {
        if (!Resolve(uid, ref oxygenation, logMissing: false))
            return;

        if (oxygenation.CardiacArrest == arrest)
            return;

        oxygenation.CardiacArrest = arrest;

        if (arrest)
        {
            oxygenation.PulseRate = 0;
            oxygenation.Oxygenation = Math.Min(oxygenation.Oxygenation, 0.5f);

            // Stop the heart
            if (TryComp<BodyComponent>(uid, out var body) && body.Organs != null)
            {
                foreach (var organ in body.Organs.ContainedEntities)
                {
                    if (TryComp<HeartConditionComponent>(organ, out var heart))
                    {
                        heart.Beating = false;
                        Dirty(organ, heart);
                    }
                }
            }
        }

        Dirty(uid, oxygenation);
    }

    /// <summary>
    /// Restart the heart, ending cardiac arrest.
    /// </summary>
    public bool RestartHeart(EntityUid uid)
    {
        if (!TryComp<BloodOxygenationComponent>(uid, out var oxygenation))
            return false;

        if (!oxygenation.CardiacArrest)
            return false;

        // Check underlying cause - if O2 is too low, heart will just stop again
        if (oxygenation.Oxygenation < O2_THRESHOLD_BRAIN_DAMAGE)
            return false;

        oxygenation.CardiacArrest = false;
        oxygenation.PulseRate = oxygenation.BasePulse;

        // Restart the heart organ
        if (TryComp<BodyComponent>(uid, out var body) && body.Organs != null)
        {
            foreach (var organ in body.Organs.ContainedEntities)
            {
                if (TryComp<HeartConditionComponent>(organ, out var heart))
                {
                    heart.Beating = true;
                    Dirty(organ, heart);
                }
            }
        }

        Dirty(uid, oxygenation);
        return true;
    }
}
