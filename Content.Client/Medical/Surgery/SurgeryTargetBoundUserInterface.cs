using Content.Shared.Medical.Surgery;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Medical.Surgery;

[UsedImplicitly]
public sealed class SurgeryTargetBoundUserInterface : BoundUserInterface
{
    private SurgeryTargetWindow? _window;

    public SurgeryTargetBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<SurgeryTargetWindow>();
        _window.Title = "Surgery Target";
        _window.OnZoneSelected += zone =>
        {
            SendMessage(new SetSurgeryTargetMessage(zone));
        };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Close();
    }
}
