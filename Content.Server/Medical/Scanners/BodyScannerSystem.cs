using Content.Server.Medical.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Medical.Scanners;
using Content.Shared.MedicalScanner;

namespace Content.Server.Medical.Scanners;

public sealed partial class BodyScannerSystem : SharedBodyScannerSystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private HealthAnalyzerSystem _healthAnalyzerSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyScannerComponent, ComponentStartup>(OnComponentStartup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = Timing.CurTime;
        var query = EntityQueryEnumerator<BodyScannerComponent>();

        while (query.MoveNext(out var uid, out var scanner))
        {
            if (curTime < scanner.NextUpdateTime)
                continue;

            scanner.NextUpdateTime += scanner.UpdateInterval;
            Dirty(uid, scanner);

            if (UI.IsUiOpen(uid, BodyScannerUiKey.Key))
                UpdateUi((uid, scanner));
        }
    }

    private void OnComponentStartup(EntityUid uid, BodyScannerComponent component, ComponentStartup args)
    {
        component.NextUpdateTime = Timing.CurTime + component.UpdateInterval;
        Dirty(uid, component);
    }

    protected override void PerformScan(Entity<BodyScannerComponent> entity)
    {
        UpdateUi(entity);
    }

    protected override void UpdateUi(Entity<BodyScannerComponent> entity)
    {
        if (!UI.IsUiOpen(entity.Owner, BodyScannerUiKey.Key))
            return;

        var patient = entity.Comp.BodyContainer.ContainedEntity;
        var health = _healthAnalyzerSystem.GetHealthAnalyzerUiState(patient);
        health.ScanMode = true;

        var scanData = new List<string>();

        if (patient != null)
        {
            scanData.Add(Loc.GetString("body-scanner-scan-brain", ("activity", health.BrainActivity)));
            scanData.Add(Loc.GetString("body-scanner-scan-pulse", ("rate", health.PulseRate)));
            scanData.Add(Loc.GetString("body-scanner-scan-oxygen", ("oxygenation", health.BloodOxygenation)));

            if (health.Limbs != null)
            {
                foreach (var limb in health.Limbs)
                {
                    var text = Loc.GetString("body-scanner-scan-limb",
                        ("limb", limb.Name),
                        ("brute", limb.BruteDamage),
                        ("burn", limb.BurnDamage));
                    if (limb.Fractured) text += " " + Loc.GetString("body-scanner-scan-fractured");
                    if (limb.Bleeding) text += " " + Loc.GetString("body-scanner-scan-bleeding");
                    scanData.Add(text);
                }
            }

            if (health.HasFractures)
                scanData.Add(Loc.GetString("body-scanner-scan-fractures-detected"));
            if (health.HasInternalBleeding)
                scanData.Add(Loc.GetString("body-scanner-scan-internal-bleeding"));
            if (health.HasOrganFailure)
                scanData.Add(Loc.GetString("body-scanner-scan-organ-failure"));

            if (health.Reagents != null)
            {
                foreach (var reagent in health.Reagents)
                    scanData.Add(Loc.GetString("body-scanner-scan-reagent", ("name", reagent.Name), ("amount", reagent.Quantity)));
            }
        }

        UI.ServerSendUiMessage(
            entity.Owner,
            BodyScannerUiKey.Key,
            new BodyScannerUserMessage(health, patient != null, true, scanData)
        );
    }
}
