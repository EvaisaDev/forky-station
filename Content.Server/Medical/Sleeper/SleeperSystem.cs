using Content.Server.Medical.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Medical.Sleeper;
using Content.Shared.MedicalScanner;

namespace Content.Server.Medical.Sleeper;

public sealed partial class SleeperSystem : SharedSleeperSystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private HealthAnalyzerSystem _healthAnalyzerSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SleeperComponent, ComponentStartup>(OnComponentStartup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SleeperComponent>();

        while (query.MoveNext(out var uid, out var sleeper))
        {
            UpdateAppearance(uid, sleeper);

            if (Timing.CurTime < sleeper.NextUiUpdateTime)
                continue;

            sleeper.NextUiUpdateTime += sleeper.UiUpdateInterval;
            Dirty(uid, sleeper);
            UpdateUi((uid, sleeper));
        }
    }

    private void OnComponentStartup(EntityUid uid, SleeperComponent component, ComponentStartup args)
    {
        component.NextInjectionTime = Timing.CurTime + component.BeakerTransferTime;
        component.NextUiUpdateTime = Timing.CurTime + component.UiUpdateInterval;
        Dirty(uid, component);
        UpdateAppearance(uid, component);
    }

    protected override void UpdateUi(Entity<SleeperComponent> entity)
    {
        if (!UI.IsUiOpen(entity.Owner, SleeperUiKey.Key))
            return;

        var patient = entity.Comp.BodyContainer.ContainedEntity;
        var (beakerCapacity, beaker) = GetBeakerInfo(entity);
        var injecting = GetInjectingReagents(entity);
        var health = _healthAnalyzerSystem.GetHealthAnalyzerUiState(patient);
        health.ScanMode = true;

        UI.ServerSendUiMessage(
            entity.Owner,
            SleeperUiKey.Key,
            new SleeperUserMessage(
                health,
                beakerCapacity,
                beaker,
                injecting,
                entity.Comp.Filtering,
                entity.Comp.Pump,
                entity.Comp.StasisSetting,
                entity.Comp.StasisSettings,
                entity.Comp.AvailableChemicals,
                patient != null,
                entity.Comp.Locked)
        );
    }
}
