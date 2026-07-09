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

            // Tended wounds heal faster
            var tended = effects.GetEffect("Tendable", _prototype) is { } tend
                && tend.GetFloat("tended") > 0;
            var healRate = healPerTick * (tended ? 2 : 1);

            var totalDamage = wound.Damage.GetTotal();
            if (totalDamage <= 0)
            {
                RemoveWound(uid, wound);
                continue;
            }

            var raw = (float)totalDamage.Float();
            foreach (var (type, value) in wound.Damage.DamageDict)
            {
                if (value > 0)
                {
                    var reduction = FixedPoint2.New((float)value.Float() * (healRate / raw));
                    wound.Damage.DamageDict[type] = value - reduction;
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
