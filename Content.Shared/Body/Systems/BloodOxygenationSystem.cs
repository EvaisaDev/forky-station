using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.CPR;
using Content.Shared.Medical.Pain;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Network;

namespace Content.Shared.Body.Systems;

/// <summary>
///     Baystation-style blood oxygenation and pulse system.
///     Pulse uses discrete levels (0-5) with probabilistic heart damage and cardiac arrest.
/// </summary>
public sealed partial class BloodOxygenationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private BrainSystem _brain = default!;
    [Dependency] private INetManager _net = default!;

    // Baystation pulse levels (PULSE_NONE=0 through PULSE_THREADY=5)
    public const int PULSE_NONE = 0;     // cardiac arrest
    public const int PULSE_SLOW = 1;     // ~50 BPM
    public const int PULSE_NORM = 2;     // ~72 BPM (normal)
    public const int PULSE_FAST = 3;     // ~100 BPM
    public const int PULSE_2FAST = 4;    // ~150 BPM (1%/tick heart damage)
    public const int PULSE_THREADY = 5;  // ~200+ BPM (5%/tick heart damage, 5%/tick cardiac arrest)

    public const float O2_THRESHOLD_BRAIN_DAMAGE = 0.85f;
    public const float O2_THRESHOLD_LETHAL = 0.30f;
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
        if (!_net.IsServer)
            return;

        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<BloodOxygenationComponent>();
        while (query.MoveNext(out var uid, out var oxygenation))
        {
            if (curTime < oxygenation.NextUpdate)
                continue;

            oxygenation.NextUpdate = curTime + UpdateInterval;
            ProcessOxygenation(uid, oxygenation);
        }
    }

    private void OnMapInit(Entity<BloodOxygenationComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdate = _timing.CurTime + UpdateInterval;
        ent.Comp.Oxygenation = 1.0f;
        ent.Comp.PulseLevel = PULSE_NORM;
        ent.Comp.PulseRate = PulseLevelToBPM(PULSE_NORM);
        ent.Comp.CardiacArrest = false;
    }

    private void OnTerminating(Entity<BloodOxygenationComponent> ent, ref EntityTerminatingEvent args)
    {
    }

    private void ProcessOxygenation(EntityUid uid, BloodOxygenationComponent oxygenation)
    {
        if (_mobState.IsDead(uid))
            return;

        if (oxygenation.CardiacArrest)
        {
            if (TryComp<CPRComponent>(uid, out var cpr) && cpr.Active)
            {
                var cprOutput = cpr.CardiacOutputModifier;
                var cprBloodVolume = GetBloodVolumeRatio(uid);
                var cprLungEff = GetLungEfficiency(uid);
                oxygenation.Oxygenation = cprBloodVolume * cprLungEff * cprOutput + GetDexalinBonus(uid);
                oxygenation.Oxygenation = Math.Clamp(oxygenation.Oxygenation, 0, 1);
                oxygenation.PulseLevel = PULSE_SLOW;
                oxygenation.PulseRate = PulseLevelToBPM(PULSE_SLOW);
                ApplyOxygenEffects(uid, oxygenation);
            }
            else
            {
                oxygenation.Oxygenation = Math.Max(0, oxygenation.Oxygenation - 0.05f);
                oxygenation.PulseLevel = PULSE_NONE;
                oxygenation.PulseRate = 0;
                ApplyOxygenEffects(uid, oxygenation);
            }
            return;
        }

        var bloodVolume = GetBloodVolumeRatio(uid);
        var lungEff = GetLungEfficiency(uid);
        var heartEff = GetHeartEfficiency(uid);
        var dexalinBonus = GetDexalinBonus(uid);

        oxygenation.Oxygenation = bloodVolume * lungEff * heartEff + dexalinBonus;
        oxygenation.Oxygenation = Math.Clamp(oxygenation.Oxygenation, 0, 1);

        // Baystation pulse level calculation
        oxygenation.PulseLevel = CalculatePulseLevel(uid, oxygenation);

        // Apply probabilistic heart damage at high pulse (Baystation model)
        if (oxygenation.PulseLevel >= PULSE_2FAST)
        {
            // PULSE_2FAST: 1% chance/tick of heart damage
            if (oxygenation.PulseLevel == PULSE_2FAST && _random.Prob(0.01f))
                DamageHeart(uid);

            // PULSE_THREADY: 5% chance/tick of heart damage
            if (oxygenation.PulseLevel == PULSE_THREADY && _random.Prob(0.05f))
                DamageHeart(uid);

            // PULSE_THREADY: 5% chance/tick of cardiac arrest
            if (oxygenation.PulseLevel == PULSE_THREADY && _random.Prob(0.05f))
            {
                SetCardiacArrest(uid, oxygenation, true);
                return;
            }
        }

        // Convert pulse level to display BPM
        oxygenation.PulseRate = PulseLevelToBPM(oxygenation.PulseLevel);

        ApplyOxygenEffects(uid, oxygenation);
    }

    /// <summary>
    ///     Computes pulse level (0-5) using Baystation's model.
    ///     Base = PULSE_NORM (2).
    ///     +1 from low O2, +1/2 from shock, chemicals can modify.
    ///     Inaprovaline (CE_STABLE) pulls toward PULSE_NORM.
    /// </summary>
    private int CalculatePulseLevel(EntityUid uid, BloodOxygenationComponent oxy)
    {
        // Start at normal
        var pulseMod = 0;

        // Shock contribution (Baystation: +1 at shock > 30, +1 at shock > 80)
        if (TryComp<PainComponent>(uid, out var pain))
        {
            if (pain.ShockLevel > 80)
                pulseMod += 2;
            else if (pain.ShockLevel > 30)
                pulseMod += 1;
        }

        // Low O2 contribution (Baystation: +1 at O2 < 70%, +1 at O2 < 60%)
        if (oxy.Oxygenation < 0.60f)
            pulseMod += 2;
        else if (oxy.Oxygenation < 0.70f)
            pulseMod += 1;

        // Clamp to valid range (0-5, but 0 only if stopped)
        var pulseLevel = Math.Clamp(PULSE_NORM + pulseMod, PULSE_SLOW, PULSE_THREADY);

        // Inaprovaline stabilization: pull toward PULSE_NORM
        if (HasInaprovaline(uid) && pulseLevel != PULSE_NORM)
        {
            if (pulseLevel > PULSE_NORM)
                pulseLevel--;
            else
                pulseLevel++;
        }

        return pulseLevel;
    }

    /// <summary>
    ///     Converts Baystation pulse level to approximate BPM for display.
    /// </summary>
    private static float PulseLevelToBPM(int level)
    {
        return level switch
        {
            PULSE_NONE => 0,
            PULSE_SLOW => 50,
            PULSE_NORM => 72,
            PULSE_FAST => 100,
            PULSE_2FAST => 150,
            PULSE_THREADY => 200,
            _ => 72
        };
    }

    /// <summary>
    ///     Directly damage the heart organ (from high pulse strain).
    /// </summary>
    private void DamageHeart(EntityUid uid)
    {
        if (!TryComp<BodyComponent>(uid, out var body) || body.Organs == null)
            return;

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (TryComp<HeartConditionComponent>(organ, out var heart))
            {
                heart.Efficiency = Math.Max(0, heart.Efficiency - 0.05f);
                Dirty(organ, heart);
                return;
            }
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

        var dexalinAmount = bloodSolution.GetTotalPrototypeQuantity("DexalinPlus");
        if (dexalinAmount > 0)
            return DEXALIN_PLUS_BONUS;

        dexalinAmount = bloodSolution.GetTotalPrototypeQuantity("Dexalin");
        if (dexalinAmount > 0)
            return DEXALIN_BONUS;

        return 0f;
    }

    private bool HasInaprovaline(EntityUid uid)
    {
        if (!TryComp<BloodstreamComponent>(uid, out var stream))
            return false;

        if (!_solution.ResolveSolution(uid, stream.BloodSolutionName, ref stream.BloodSolution, out var blood))
            return false;

        return blood.GetTotalPrototypeQuantity("Inaprovaline") > 0;
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
            oxygenation.PulseLevel = PULSE_NONE;
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
        if (oxygenation.Oxygenation < O2_THRESHOLD_LETHAL)
            return false;

        oxygenation.CardiacArrest = false;
        oxygenation.PulseLevel = PULSE_SLOW;
        oxygenation.PulseRate = PulseLevelToBPM(PULSE_SLOW);

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

        return true;
    }
}
