using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Cloning.Events;

namespace Content.Server.Medical;

public sealed partial class CloningBrainIntegrationSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyComponent, CloningAttemptEvent>(OnCloningAttempt);
    }

    private void OnCloningAttempt(Entity<BodyComponent> ent, ref CloningAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (ent.Comp.Organs == null)
            return;

        foreach (var organ in ent.Comp.Organs.ContainedEntities)
        {
            if (TryComp<BrainComponent>(organ, out var brain))
            {
                if (brain.HasBeenDead)
                {
                    args.Cancelled = true;
                    return;
                }
                return;
            }
        }
    }
}
