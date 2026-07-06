using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.DragDrop;
using Robust.Shared.Timing;

namespace Content.Shared.Medical.Machines;

public abstract partial class SharedVitalsMonitorSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] protected SharedAppearanceSystem Appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VitalsMonitorComponent, DragDropTargetEvent>(OnDragDrop);
        SubscribeLocalEvent<VitalsMonitorComponent, CanDropTargetEvent>(OnCanDrop);
    }

    private void OnDragDrop(Entity<VitalsMonitorComponent> ent, ref DragDropTargetEvent args)
    {
        if (ent.Comp.Connected)
            DisconnectPatient(ent.Owner, ent.Comp);
        else
            ConnectPatient(ent.Owner, args.Dragged, ent.Comp);

        args.Handled = true;
    }

    private void OnCanDrop(Entity<VitalsMonitorComponent> ent, ref CanDropTargetEvent args)
    {
        args.Handled = true;
        args.CanDrop |= HasComp<BloodOxygenationComponent>(args.Dragged);
    }

    public void ConnectPatient(EntityUid uid, EntityUid patient, VitalsMonitorComponent monitor)
    {
        if (monitor.Connected)
            return;

        monitor.ConnectedPatient = patient;
        monitor.Connected = true;
        monitor.NextUpdateTime = Timing.CurTime + monitor.UpdateInterval;
        Dirty(uid, monitor);

        UpdateVitalsTick((uid, monitor));
    }

    public void DisconnectPatient(EntityUid uid, VitalsMonitorComponent monitor)
    {
        if (!monitor.Connected)
            return;

        monitor.ConnectedPatient = null;
        monitor.Connected = false;
        monitor.PulseRate = 0;
        monitor.BloodOxygenation = 0;
        monitor.BrainActivity = "None";
        monitor.BreathingStatus = "None";
        monitor.HasCardiacArrest = false;
        monitor.HasBrainDamage = false;
        monitor.HasBreathingProblem = false;
        Dirty(uid, monitor);

        UpdateAppearance(uid, monitor);
    }

    private void UpdateVitals(EntityUid uid, VitalsMonitorComponent monitor)
    {
        if (monitor.ConnectedPatient == null || Deleted(monitor.ConnectedPatient.Value))
        {
            DisconnectPatient(uid, monitor);
            return;
        }

        var patient = monitor.ConnectedPatient.Value;

        if (!Transform(patient).Coordinates.TryDistance(EntityManager, Transform(uid).Coordinates, out var distance) || distance > 3f)
        {
            DisconnectPatient(uid, monitor);
            return;
        }

        if (!TryComp<BloodOxygenationComponent>(patient, out var oxy))
        {
            monitor.PulseRate = 0;
            monitor.BloodOxygenation = 0;
            monitor.BrainActivity = "None";
            monitor.HasCardiacArrest = true;
        }
        else
        {
            monitor.PulseRate = oxy.PulseRate;
            monitor.BloodOxygenation = oxy.Oxygenation * 100f;
            monitor.HasCardiacArrest = oxy.CardiacArrest;

            if (TryComp<BrainComponent>(patient, out var brain))
            {
                var pct = (float)(brain.Integrity / brain.MaxIntegrity).Float();
                monitor.BrainActivity = pct <= 0 ? "None" : pct < 0.3f ? "Critical" : pct < 0.6f ? "Weak" : "Normal";
                monitor.HasBrainDamage = pct < 0.6f;
            }
            else
            {
                monitor.BrainActivity = "Normal";
                monitor.HasBrainDamage = false;
            }

            if (TryComp<BodyComponent>(patient, out var body) && body.Organs != null)
            {
                var worstLung = 1f;
                foreach (var organ in body.Organs.ContainedEntities)
                {
                    if (TryComp<LungConditionComponent>(organ, out var lung))
                        worstLung = Math.Min(worstLung, lung.Efficiency);
                }

                monitor.BreathingStatus = worstLung >= 0.8f ? "Normal" : worstLung >= 0.4f ? "Shallow" : "NotBreathing";
                monitor.HasBreathingProblem = worstLung < 0.8f;
            }
            else
            {
                monitor.BreathingStatus = "Normal";
                monitor.HasBreathingProblem = false;
            }
        }

        Dirty(uid, monitor);
    }

    private void UpdateAppearance(EntityUid uid, VitalsMonitorComponent monitor)
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        if (!monitor.Connected)
        {
            Appearance.SetData(uid, VitalsMonitorVisuals.PulseStatus, VitalsPulseStatus.None, appearance);
            Appearance.SetData(uid, VitalsMonitorVisuals.BrainStatus, VitalsBrainStatus.None, appearance);
            Appearance.SetData(uid, VitalsMonitorVisuals.BreathingStatus, VitalsBreathingStatus.None, appearance);
            return;
        }

        if (monitor.HasCardiacArrest)
            Appearance.SetData(uid, VitalsMonitorVisuals.PulseStatus, VitalsPulseStatus.Flatline, appearance);
        else if (monitor.PulseRate >= 150)
            Appearance.SetData(uid, VitalsMonitorVisuals.PulseStatus, VitalsPulseStatus.Threading, appearance);
        else if (monitor.PulseRate >= 100)
            Appearance.SetData(uid, VitalsMonitorVisuals.PulseStatus, VitalsPulseStatus.Fast, appearance);
        else if (monitor.PulseRate > 0)
            Appearance.SetData(uid, VitalsMonitorVisuals.PulseStatus, VitalsPulseStatus.Normal, appearance);
        else
            Appearance.SetData(uid, VitalsMonitorVisuals.PulseStatus, VitalsPulseStatus.None, appearance);

        if (monitor.BrainActivity == "None")
            Appearance.SetData(uid, VitalsMonitorVisuals.BrainStatus, VitalsBrainStatus.Warning, appearance);
        else if (monitor.BrainActivity == "Critical")
            Appearance.SetData(uid, VitalsMonitorVisuals.BrainStatus, VitalsBrainStatus.Critical, appearance);
        else if (monitor.BrainActivity == "Weak")
            Appearance.SetData(uid, VitalsMonitorVisuals.BrainStatus, VitalsBrainStatus.Weak, appearance);
        else
            Appearance.SetData(uid, VitalsMonitorVisuals.BrainStatus, VitalsBrainStatus.Normal, appearance);

        if (monitor.BreathingStatus == "NotBreathing")
            Appearance.SetData(uid, VitalsMonitorVisuals.BreathingStatus, VitalsBreathingStatus.NotBreathing, appearance);
        else if (monitor.BreathingStatus == "Shallow")
            Appearance.SetData(uid, VitalsMonitorVisuals.BreathingStatus, VitalsBreathingStatus.Shallow, appearance);
        else
            Appearance.SetData(uid, VitalsMonitorVisuals.BreathingStatus, VitalsBreathingStatus.Normal, appearance);
    }

    public void UpdateVitalsTick(Entity<VitalsMonitorComponent> entity)
    {
        if (!entity.Comp.Connected || entity.Comp.ConnectedPatient == null)
            return;

        UpdateVitals(entity.Owner, entity.Comp);
        UpdateAppearance(entity.Owner, entity.Comp);
    }
}
