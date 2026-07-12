using Content.Shared.Body;
using Content.Shared.Body.Organs;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Medical.Wounds;

public sealed partial class InfectionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

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

                // Necrotic organ: germ level > 80 causes necrosis
                if (germLevel > 80 && TryComp<ExternalOrganComponent>(parent, out var ext)
                    && (ext.Status & OrganStatusFlags.Dead) == 0
                    && (ext.Status & OrganStatusFlags.Robotic) == 0)
                {
                    ext.Status |= OrganStatusFlags.Dead;
                    Dirty(parent, ext);
                    var msg = Loc.GetString("limb-necrotic", ("limb", Name(parent)));
                    // Send popup to the body owner
                    if (TryComp<OrganComponent>(parent, out var org) && org.Body is { } bodyEnt)
                        _popup.PopupEntity(msg, parent, bodyEnt, PopupType.Medium);
                }
            }

            Dirty(uid, effects);
        }
    }
}
