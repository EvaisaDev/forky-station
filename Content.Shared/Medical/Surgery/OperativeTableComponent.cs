using Robust.Shared.GameStates;

namespace Content.Shared.Medical.Surgery;

/// <summary>
///     Marks an entity as an operating table. Provides surgery bonuses
///     and enables anesthesia for the buckled patient.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OperativeTableComponent : Component
{
    /// <summary>
    ///     Whether anesthesia (neural suppressors) is active.
    ///     When active, the buckled patient is kept unconscious and still.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool AnesthesiaActive;

    /// <summary>
    ///     Surgery success bonus when on this table (1.0 = no bonus, higher = bonus).
    ///     Improvised tables (roller beds) have lower values.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SurgeryQualityBonus = 1.0f;

    /// <summary>
    ///     Whether this table prevents wound infection during surgery.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool SterileEnvironment = true;
}
