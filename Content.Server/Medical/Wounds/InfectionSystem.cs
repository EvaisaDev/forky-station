using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Medical.Wounds;

public sealed partial class InfectionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

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
            if (!TryComp<WoundEffectsComponent>(uid, out var effects))
                continue;

            var germInstance = effects.GetEffect("Germ", _prototype);
            if (germInstance == null)
                continue;

            var tended = effects.GetEffect("Tendable", _prototype) is { } tend
                && tend.GetFloat("tended") > 0;

            if (!tended)
            {
                germInstance.SetFloat("germLevel", germInstance.GetFloat("germLevel") + 1);
            }
            else
            {
                germInstance.SetFloat("germLevel", Math.Max(0, germInstance.GetFloat("germLevel") - 1));
            }

            var germLevel = Math.Clamp(germInstance.GetFloat("germLevel"), 0, 100);
            germInstance.SetFloat("germLevel", germLevel);

            if (germLevel <= 0)
                continue;

            if (germLevel > 20 && wound.ParentWoundable is { } parent)
            {
                var damage = new DamageSpecifier();
                damage.DamageDict.Add("Poison", FixedPoint2.New(germLevel * 0.05f));
                _damageable.TryChangeDamage(parent, damage, interruptsDoAfters: false);
            }

            Dirty(uid, effects);
        }
    }
}
