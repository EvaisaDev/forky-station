using Content.Shared.Medical.Sleeper;
using Robust.Client.GameObjects;

namespace Content.Client.Medical.Sleeper;

public sealed partial class SleeperSystem : SharedSleeperSystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SleeperComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(EntityUid uid, SleeperComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!Appearance.TryGetData<bool>(uid, SleeperVisuals.ContainsEntity, out var hasOccupant, args.Component))
        {
            hasOccupant = false;
        }

        _sprite.LayerSetRsiState((uid, args.Sprite), SleeperVisualLayers.Base, hasOccupant ? "sleeper_working" : "sleeper");
    }

    protected override void UpdateUi(Entity<SleeperComponent> sleeper)
    {
    }
}

public enum SleeperVisualLayers : byte
{
    Base,
}
