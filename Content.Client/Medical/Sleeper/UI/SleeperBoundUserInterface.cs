using Content.Shared.FixedPoint;
using Content.Shared.Medical.Sleeper;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Medical.Sleeper.UI;

[UsedImplicitly]
public sealed class SleeperBoundUserInterface : BoundUserInterface
{
    private SleeperWindow? _window;

    public SleeperBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<SleeperWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;

        _window.OnEjectPatientPressed += () => SendMessage(new SleeperEjectPatientMessage());
        _window.OnEjectBeakerPressed += () => SendMessage(new SleeperEjectBeakerMessage());
        _window.OnToggleFilter += filtering => SendMessage(new SleeperToggleFilterMessage(filtering));
        _window.OnTogglePump += pump => SendMessage(new SleeperTogglePumpMessage(pump));
        _window.OnSetStasis += stasis => SendMessage(new SleeperSetStasisMessage(stasis));
        _window.OnInjectChemical += (chemical, amount) => SendMessage(new SleeperInjectChemicalMessage(chemical, amount));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (_window != null && message is SleeperUserMessage msg)
            _window.Populate(msg);
    }
}
