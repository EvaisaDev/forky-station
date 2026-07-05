using Robust.Shared.GameStates;

namespace Content.Shared.Medical.Wounds;

/// <summary>
///     Tracks embedded objects in a wound (shrapnel, bullets, etc.).
///     Embedded objects prevent natural healing and cause ongoing pain.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EmbeddedObjectComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<string> EmbeddedItems = new();
}
