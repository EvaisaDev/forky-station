using Content.Shared._Medical.Targeting;

namespace Content.Server._Medical.Targeting;

public sealed class TargetingSystem : SharedTargetingSystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<TargetChangeEvent>(OnTargetChange);
    }

    private void OnTargetChange(TargetChangeEvent message, EntitySessionEventArgs args)
    {
        if (!TryComp<TargetingComponent>(GetEntity(message.Uid), out var target))
            return;

        target.ActivePart = message.BodyPart;
        Dirty(GetEntity(message.Uid), target);
    }
}
