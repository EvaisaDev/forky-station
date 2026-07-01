using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared.Body.Components;

/// <summary>
///     Tracks brain integrity (0-100%).
///     When brain integrity reaches 0%, the entity is considered brain-dead.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(BrainSystem))]
public sealed partial class BrainComponent : Component
{
    /// <summary>
    ///     Current brain integrity, ranging from 0 to MaxIntegrity.
    ///     0 = brain dead, MaxIntegrity = perfectly healthy.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 Integrity = FixedPoint2.New(100);

    /// <summary>
    ///     Maximum possible brain integrity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 MaxIntegrity = FixedPoint2.New(100);

    /// <summary>
    ///     Minimum integrity before brain death occurs.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 MinDeadIntegrity;

    /// <summary>
    ///     Whether this brain has ever reached 0 integrity.
    ///     Used to determine if cloning is possible.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool HasBeenDead;
}
