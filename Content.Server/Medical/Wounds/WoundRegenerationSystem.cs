using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Medical.Wounds;

public sealed partial class WoundRegenerationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    private TimeSpan _nextUpdate;
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        if (curTime < _nextUpdate)
            return;

        _nextUpdate = curTime + UpdateInterval;

        var query = EntityQueryEnumerator<WoundComponent>();
        while (query.MoveNext(out var uid, out var wound))
        {
            if (wound.IsSurgical)
                continue;

            if (!TryComp<WoundEffectsComponent>(uid, out var effects))
                continue;

            // Embedded objects prevent natural healing
            var embedded = effects.GetEffect("Embedded", _prototype);
            if (embedded is { StringListParams: { Count: > 0 } })
                continue;

            var healable = effects.GetEffect("Healable", _prototype);
            if (healable == null)
                continue;

            var healPerTick = healable.GetFloatOrConfig("healPerTick", _prototype);
            if (healPerTick <= 0)
                continue;

            var totalDamage = wound.Damage.GetTotal();
            if (totalDamage <= 0)
            {
                RemoveWound(uid, wound);
                continue;
            }

            // Baystation-style autoheal cutoff: wounds above this damage don't heal unless treated
            var autohealCutoff = healable.GetFloatOrConfig("autohealCutoff", _prototype);
            var currentDmg = (float)totalDamage.Float();
            if (autohealCutoff > 0 && currentDmg > autohealCutoff)
            {
                // Check if the wound is treated (tended for cuts/pierce, salved for burns)
                var treated = effects.GetEffect("Tendable", _prototype) is { } tend
                    && tend.GetFloat("tended") > 0;
                if (!treated && effects.GetEffect("Salvable", _prototype) is { } salveEff)
                    treated = salveEff.GetFloat("salved") > 0;
                if (!treated)
                    continue;
            }

            // Tended wounds heal twice as fast
            var tended = effects.GetEffect("Tendable", _prototype) is { } tendEff
                && tendEff.GetFloat("tended") > 0;
            var healRate = healPerTick * (tended ? 2 : 1);

            var raw = (float)totalDamage.Float();
            foreach (var (type, value) in wound.Damage.DamageDict)
            {
                if (value > 0)
                {
                    var reduction = FixedPoint2.New((float)value.Float() * (healRate / raw));
                    wound.Damage.DamageDict[type] = value - reduction;
                }
            }

            // Decrement bleed timer (Baystation: wounds naturally stop bleeding over time)
            var bleedInstance = effects.GetEffect("Bleeding", _prototype);
            if (bleedInstance != null)
            {
                var bleedTimer = bleedInstance.GetFloat("bleedTimer");
                if (bleedTimer > 0)
                {
                    bleedTimer -= 1;
                    if (bleedTimer < 0)
                        bleedTimer = 0;
                    bleedInstance.SetFloat("bleedTimer", bleedTimer);
                }
            }

            var maxDmg = (float)wound.MaximumDamage.Float();
            var curDmg = (float)wound.Damage.GetTotal().Float();
            var pct = maxDmg > 0 ? curDmg / maxDmg : 0;

            wound.Stage = pct switch
            {
                > 0.80f => 0,
                > 0.60f => 1,
                > 0.40f => 2,
                > 0.20f => 3,
                > 0f   => 4,
                _      => wound.MaxStages - 1
            };

            Dirty(uid, wound);
        }
    }

    private void RemoveWound(EntityUid uid, WoundComponent wound)
    {
        QueueDel(uid);

        if (wound.ParentWoundable is { } parent && TryComp<WoundableComponent>(parent, out var wc))
        {
            wc.Wounds.Remove(uid);
            Dirty(parent, wc);
        }
    }
}
