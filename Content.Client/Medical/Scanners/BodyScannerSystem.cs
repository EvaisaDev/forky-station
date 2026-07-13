using Content.Shared.Medical.Scanners;
using Robust.Client.GameObjects;

namespace Content.Client.Medical.Scanners;

public sealed partial class BodyScannerSystem : SharedBodyScannerSystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyScannerComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(EntityUid uid, BodyScannerComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!Appearance.TryGetData<BodyScannerStatus>(uid, BodyScannerVisuals.Status, out var status, args.Component))
        {
            status = BodyScannerStatus.Open;
        }

        var state = status switch
        {
            BodyScannerStatus.Off => "body_scanner_closed",
            BodyScannerStatus.Open => "body_scanner",
            BodyScannerStatus.Green => "body_scanner_working",
            BodyScannerStatus.Red => "body_scanner_working",
            BodyScannerStatus.Death => "body_scanner_closed",
            BodyScannerStatus.Yellow => "body_scanner_working",
            _ => "body_scanner"
        };

        _sprite.LayerSetRsiState((uid, args.Sprite), BodyScannerVisualLayers.Base, state);
    }

    protected override void PerformScan(Entity<BodyScannerComponent> entity)
    {
    }

    protected override void UpdateUi(Entity<BodyScannerComponent> scanner)
    {
    }
}

public enum BodyScannerVisualLayers : byte
{
    Base,
}
