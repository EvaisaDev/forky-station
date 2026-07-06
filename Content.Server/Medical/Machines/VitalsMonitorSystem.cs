using Content.Shared.Medical.Machines;

namespace Content.Server.Medical.Machines;

public sealed partial class VitalsMonitorSystem : SharedVitalsMonitorSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VitalsMonitorComponent, ComponentStartup>(OnComponentStartup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = Timing.CurTime;
        var query = EntityQueryEnumerator<VitalsMonitorComponent>();

        while (query.MoveNext(out var uid, out var monitor))
        {
            if (!monitor.Connected)
                continue;

            if (curTime < monitor.NextUpdateTime)
                continue;

            monitor.NextUpdateTime += monitor.UpdateInterval;
            Dirty(uid, monitor);

            UpdateVitalsTick((uid, monitor));
        }
    }

    private void OnComponentStartup(EntityUid uid, VitalsMonitorComponent component, ComponentStartup args)
    {
        component.NextUpdateTime = Timing.CurTime + component.UpdateInterval;
        Dirty(uid, component);
    }
}
