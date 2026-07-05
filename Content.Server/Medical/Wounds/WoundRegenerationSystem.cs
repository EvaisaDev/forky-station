using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds;
using Robust.Shared.Timing;

namespace Content.Server.Medical.Wounds;

/// <summary>
///     Advances wound stages over time as wounds heal.
///     Surgical wounds don't heal naturally — must be cauterized.
///     Tended (bandaged) wounds heal faster.
///     Fully healed wounds are removed.
/// </summary>
public sealed partial class WoundRegenerationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

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

            // Embedded objects prevent natural healing
            if (TryComp<EmbeddedObjectComponent>(uid, out var embedded) && embedded.EmbeddedItems.Count > 0)
                continue;

            if (!TryComp<HealableWoundComponent>(uid, out var healable) || healable.HealPerTick <= 0)
                continue;

            // Tended wounds heal faster
            var tended = TryComp<TendableWoundComponent>(uid, out var tend) && tend.Tended;
            var healRate = (float)healable.HealPerTick.Float() * (tended ? 2 : 1);

            // Apply healing
            var totalDamage = wound.Damage.GetTotal();
            if (totalDamage <= 0)
            {
                RemoveWound(uid, wound);
                continue;
            }

            // Reduce damage across all types
            var raw = (float)totalDamage.Float();
            foreach (var (type, value) in wound.Damage.DamageDict)
            {
                if (value > 0)
                {
                    var reduction = FixedPoint2.New((float)value.Float() * (healRate / raw));
                    wound.Damage.DamageDict[type] = value - reduction;
                }
            }

            // Advance stage based on remaining damage percentage
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
