using Content.Shared.Body.Components;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server.Medical.Stasis;

/// <summary>
/// A stasis bag that slows the occupant's metabolism by 20x
/// degrading over 40 minutes. Allows injection through the bag.
/// </summary>
public sealed partial class StasisBagSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public const float InitialStasisFactor = 20f;
    public const float DegradationRate = 0.25f;
    public const float DegradationInterval = 300f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StasisBagComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<StasisBagComponent, EntRemovedFromContainerMessage>(OnRemoved);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<StasisBagComponent>();
        while (query.MoveNext(out var uid, out var bag))
        {
            if (bag.BodyContainer?.ContainedEntities.Count == 0)
                continue;

            if (bag.BodyContainer == null)
                continue;

            // Degrade stasis factor over time
            if (bag.NextDegradationTime < _timing.CurTime)
            {
                bag.CurrentStasisFactor *= (1f - DegradationRate);
                bag.NextDegradationTime = _timing.CurTime + TimeSpan.FromSeconds(DegradationInterval);

                if (bag.CurrentStasisFactor <= 1.0f)
                {
                    bag.CurrentStasisFactor = 1.0f;
                    bag.Spent = true;
                }

                Dirty(uid, bag);
            }
        }
    }

    private void OnInserted(Entity<StasisBagComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != StasisBagComponent.BodyContainerId)
            return;

        ent.Comp.BodyContainer = args.Container as Container;
        ent.Comp.CurrentStasisFactor = InitialStasisFactor;
        ent.Comp.NextDegradationTime = _timing.CurTime + TimeSpan.FromSeconds(DegradationInterval);
        ent.Comp.Spent = false;
        Dirty(ent, ent.Comp);

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/body_bag.ogg"), ent);
    }

    private void OnRemoved(Entity<StasisBagComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != StasisBagComponent.BodyContainerId)
            return;

        ent.Comp.BodyContainer = null;
        ent.Comp.CurrentStasisFactor = 1.0f;
        Dirty(ent, ent.Comp);
    }
}
