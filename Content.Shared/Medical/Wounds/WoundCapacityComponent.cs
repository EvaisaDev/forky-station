using Robust.Shared.GameStates;

namespace Content.Shared.Medical.Wounds;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WoundCapacityComponent : Component
{
    [DataField, AutoNetworkedField]
    public int DefaultCapacity = 3;

    [DataField, AutoNetworkedField]
    public Dictionary<string, int> GroupCapacity = new();

    [DataField, AutoNetworkedField]
    public int WeaponSpecificCapacity = 1;
}
