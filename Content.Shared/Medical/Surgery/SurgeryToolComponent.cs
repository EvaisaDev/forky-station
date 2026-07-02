using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Surgery;

/// <summary>
///     Marks an item as a surgery tool with specific step capabilities.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SurgeryToolComponent : Component
{
    /// <summary>
    ///     The type of surgery action this tool performs.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public string Action = string.Empty;

    /// <summary>
    ///     Quality/success modifier for this tool (1.0 = ideal, lower = worse).
    ///     On a proper surgical tool this is 1.0.
    ///     Improvised tools have lower values (0.75, 0.5, etc.).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Quality = 1.0f;

    /// <summary>
    ///     Whether this tool is sterile. Non-sterile tools increase infection risk.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Sterile = true;

    /// <summary>
    ///     How long the surgery step takes with this tool (seconds).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float StepDuration = 3.0f;
}
