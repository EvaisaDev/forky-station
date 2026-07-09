using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Wounds;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WoundTypeOverrideComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<WoundComponent>? WoundType;
}
