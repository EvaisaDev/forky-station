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
        var uid = GetEntity(message.Uid);
        var target = EnsureComp<TargetingComponent>(uid);
        target.ActivePart = message.BodyPart;
        Dirty(uid, target);
    }
}
