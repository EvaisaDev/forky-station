using Content.Client._Medical.Targeting;
using Content.Shared._Medical.Targeting;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organs;
using Content.Shared.Medical.Wounds;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.Medical.Wounds;

/// <summary>
/// Reads damage stored in wounds for each limb
/// Displays as a coloured overlay over the TargetingDoll
/// </summary>
public sealed partial class VisualOrganWoundsSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _uiManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private Robust.Client.Player.IPlayerManager _playerManager = default!;

    private TimeSpan _nextUpdate;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        var player = _playerManager.LocalEntity;
        if (player == null)
            return;

        if (!TryComp<BodyComponent>(player.Value, out var body) || body.Organs == null)
            return;

        var control = _uiManager.GetActiveUIWidgetOrNull<TargetingControl>();
        if (control == null)
            return;

        UpdateOverlays(control, body);
    }

    private void UpdateOverlays(TargetingControl control, BodyComponent body)
    {
        foreach (var (part, btn) in control.GetBodyPartButtons())
        {
            var category = BodyPartHelper.ToOrganCategory(part);
            var (brute, burn, woundCount, hasEmbedded) = GetLimbWoundData(body, category);

            // Find or create overlay
            var overlay = FindOrCreateOverlay(btn);

            if (brute <= 0 && burn <= 0 && woundCount == 0)
            {
                overlay.Visible = false;
                continue;
            }

            overlay.Visible = true;
            var totalDamage = brute + burn;

            Color overlayColor;
            if (totalDamage > 60 || woundCount > 3)
                overlayColor = new Color(1f, 0f, 0f, 0.35f);
            else if (totalDamage > 30 || woundCount > 1)
                overlayColor = new Color(1f, 0.65f, 0f, 0.30f);
            else
                overlayColor = new Color(1f, 1f, 0f, 0.25f);

            overlay.PanelOverride = new StyleBoxFlat { BackgroundColor = overlayColor };
        }
    }

    private (float brute, float burn, int woundCount, bool hasEmbedded) GetLimbWoundData(BodyComponent body, string category)
    {
        float brute = 0, burn = 0;
        int woundCount = 0;
        bool hasEmbedded = false;

        if (body.Organs == null)
            return (0, 0, 0, false);

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (!TryComp<OrganComponent>(organ, out var organComp))
                continue;

            if (organComp.Category != category)
                continue;

            // Brute/burn damage from ExternalOrganComponent
            if (TryComp<ExternalOrganComponent>(organ, out var ext))
            {
                brute += (float)ext.BruteDamage.Float();
                burn += (float)ext.BurnDamage.Float();
            }

            // Wound count from WoundableComponent
            if (TryComp<WoundableComponent>(organ, out var wnd))
            {
                woundCount += wnd.Wounds.Count;
                foreach (var wUid in wnd.Wounds)
                {
                    if (TerminatingOrDeleted(wUid))
                        continue;

                    if (TryComp<EmbeddedObjectComponent>(wUid, out var emb) && emb.EmbeddedItems.Count > 0)
                        hasEmbedded = true;
                }
            }
        }

        return (brute, burn, woundCount, hasEmbedded);
    }

    private static PanelContainer FindOrCreateOverlay(TextureButton button)
    {
        foreach (var child in button.Children)
        {
            if (child is PanelContainer panel && panel.Name == "WoundOverlay")
                return panel;
        }

        var overlay = new PanelContainer
        {
            Name = "WoundOverlay",
            MouseFilter = Control.MouseFilterMode.Ignore,
            HorizontalAlignment = Control.HAlignment.Stretch,
            VerticalAlignment = Control.VAlignment.Stretch,
            Visible = false
        };

        overlay.AddChild(new TextureRect
        {
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            HorizontalAlignment = Control.HAlignment.Stretch,
            VerticalAlignment = Control.VAlignment.Stretch,
            ShaderOverride = null
        });

        button.AddChild(overlay);
        return overlay;
    }
}
