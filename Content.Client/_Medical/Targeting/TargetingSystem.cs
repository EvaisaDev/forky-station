using Content.Shared._Medical.Targeting;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._Medical.Targeting;

public sealed class TargetingSystem : SharedTargetingSystem
{
    [Dependency] private IPlayerManager _playerManager = default!;

    public event Action<TargetingComponent>? TargetingStartup;
    public event Action? TargetingShutdown;
    public event Action<TargetBodyPart>? TargetChange;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TargetingComponent, LocalPlayerAttachedEvent>(HandlePlayerAttached);
        SubscribeLocalEvent<TargetingComponent, LocalPlayerDetachedEvent>(HandlePlayerDetached);
        SubscribeLocalEvent<TargetingComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<TargetingComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeNetworkEvent<TargetChangeEvent>(OnTargetChange);
    }

    private void HandlePlayerAttached(Entity<TargetingComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        TargetingStartup?.Invoke(ent.Comp);
    }

    private void HandlePlayerDetached(Entity<TargetingComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        TargetingShutdown?.Invoke();
    }

    private void OnComponentStartup(Entity<TargetingComponent> ent, ref ComponentStartup args)
    {
        if (_playerManager.LocalEntity != ent.Owner)
            return;
        TargetingStartup?.Invoke(ent.Comp);
    }

    private void OnComponentShutdown(Entity<TargetingComponent> ent, ref ComponentShutdown args)
    {
        if (_playerManager.LocalEntity != ent.Owner)
            return;
        TargetingShutdown?.Invoke();
    }

    private void OnTargetChange(TargetChangeEvent ev)
    {
        if (!TryGetEntity(ev.Uid, out var uid)
            || _playerManager.LocalEntity != uid
            || !TryComp(uid, out TargetingComponent? component))
            return;

        component.ActivePart = ev.BodyPart;
    }
}
