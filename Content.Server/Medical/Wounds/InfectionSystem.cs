using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds;
using Robust.Shared.Timing;

namespace Content.Server.Medical.Wounds;

/// <summary>
/// Tracks wound infection (germ_level). Untended wounds in dirty environments
/// accumulate germs. High germ_level causes toxin damage and organ damage.
/// Spaceacillin reduces germ_level. Ointment/Sterilizine treats infection.
/// </summary>
public sealed partial class InfectionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;

    private TimeSpan _nextUpdate;
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(3);

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
            var germ = TryComp<WoundGermComponent>(uid, out var germs) ? germs : null;
            if (germ == null)
                continue;

            // Untended wounds in the open accumulate germs
            var tended = TryComp<TendableWoundComponent>(uid, out var tend) && tend.Tended;
            if (!tended)
            {
                germ.GermLevel += 1;
            }
            else
            {
                // Tended wounds slowly reduce germs
                germ.GermLevel = Math.Max(0, germ.GermLevel - 1);
            }

            // Cap germ level
            germ.GermLevel = Math.Clamp(germ.GermLevel, 0, 100);

            if (germ.GermLevel <= 0)
                continue;

            // Apply toxin damage proportional to germ level
            if (germ.GermLevel > 20 && wound.ParentWoundable is { } parent)
            {
                var damage = new DamageSpecifier();
                damage.DamageDict.Add("Poison", FixedPoint2.New(germ.GermLevel * 0.05f));
                _damageable.TryChangeDamage(parent, damage, interruptsDoAfters: false);
            }

            Dirty(uid, germ);
        }
    }
}
