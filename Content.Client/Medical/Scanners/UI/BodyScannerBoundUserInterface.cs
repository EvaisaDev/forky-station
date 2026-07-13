using Content.Shared.Medical.Scanners;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Medical.Scanners.UI;

[UsedImplicitly]
public sealed class BodyScannerBoundUserInterface : BoundUserInterface
{
    private BodyScannerWindow? _window;

    public BodyScannerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<BodyScannerWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;

        _window.OnScanPressed += () => SendMessage(new BodyScannerScanMessage());
        _window.OnEjectPressed += () => SendMessage(new BodyScannerEjectMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (_window != null && message is BodyScannerUserMessage msg)
            _window.Populate(msg);
    }
}
