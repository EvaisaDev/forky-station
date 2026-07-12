using Content.Shared.Body.Components;
using Content.Shared.Body.Organs;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Wounds;

public sealed partial class WoundExamineSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WoundableComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ExternalOrganComponent, ExaminedEvent>(OnExternalOrganExamined);
    }

    private void OnExamined(Entity<WoundableComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var wounds = ent.Comp.Wounds;
        if (wounds == null || wounds.Count == 0)
            return;

        var hasEmbedded = false;
        var woundCount = 0;

        foreach (var woundUid in wounds)
        {
            if (TerminatingOrDeleted(woundUid))
                continue;

            woundCount++;

            if (TryComp<WoundEffectsComponent>(woundUid, out var effects))
            {
                var embedded = effects.GetEffect("Embedded", _prototype);
                if (embedded is { StringListParams: { Count: > 0 } })
                    hasEmbedded = true;
            }
        }

        if (woundCount > 0)
        {
            args.PushMarkup(Loc.GetString("wound-examine-wounds-visible", ("count", woundCount)));
        }

        if (hasEmbedded)
            args.PushMarkup(Loc.GetString("wound-examine-embedded-objects"));
    }

    private void OnExternalOrganExamined(Entity<ExternalOrganComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var ext = ent.Comp;

        if ((ext.Status & OrganStatusFlags.Broken) != 0)
            args.PushMarkup(Loc.GetString("wound-examine-broken", ("limb", Name(ent))));

        if ((ext.Status & OrganStatusFlags.CutAway) != 0)
            args.PushMarkup(Loc.GetString("wound-examine-missing", ("limb", Name(ent))));

        if ((ext.Status & OrganStatusFlags.ArteryCut) != 0)
            args.PushMarkup(Loc.GetString("wound-examine-artery-cut", ("limb", Name(ent))));

        if ((ext.Status & OrganStatusFlags.TendonCut) != 0)
            args.PushMarkup(Loc.GetString("wound-examine-tendon-cut", ("limb", Name(ent))));

        if (ext.Dislocated)
            args.PushMarkup(Loc.GetString("wound-examine-dislocated", ("limb", Name(ent))));

        if (ext.Disfigured)
            args.PushMarkup(Loc.GetString("wound-examine-disfigured", ("limb", Name(ent))));

        if ((ext.Status & OrganStatusFlags.Dead) != 0)
            args.PushMarkup(Loc.GetString("wound-examine-necrotic", ("limb", Name(ent))));

        if ((ext.Status & OrganStatusFlags.Bleeding) != 0)
            args.PushMarkup(Loc.GetString("wound-examine-bleeding", ("limb", Name(ent))));
    }
}
