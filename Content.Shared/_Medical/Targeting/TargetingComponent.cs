using Robust.Shared.GameStates;

namespace Content.Shared._Medical.Targeting;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TargetingComponent : Component
{
    [DataField, AutoNetworkedField]
    public TargetBodyPart ActivePart = TargetBodyPart.Chest;
}
