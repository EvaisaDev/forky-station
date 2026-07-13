using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Body.Components;

/// <summary>
/// Defines the blood type of an entity and provides compatibility checking for cross reactions.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BloodTypeComponent : Component
{
    /// <summary>
    /// The blood type group (A, B, AB, O).
    /// </summary>
    [DataField, AutoNetworkedField]
    public BloodGroup Group = BloodGroup.O;

    /// <summary>
    /// Whether the Rh factor is positive.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool RhPositive = true;

    /// <summary>
    /// Returns a string representation (e.g., "A+", "O-").
    /// </summary>
    public string DisplayString => $"{Group}{(RhPositive ? "+" : "-")}";

    /// <summary>
    /// Checks if this blood type is compatible with a donor blood type (recipient is this, donor is the argument).
    /// O- is universal donor. AB+ is universal recipient.
    /// </summary>
    public bool IsCompatibleWith(BloodGroup donorGroup, bool donorRhPositive)
    {
        // AB can receive from anyone
        if (Group == BloodGroup.AB)
        {
            // Rh- can receive Rh- only, Rh+ can receive both
            if (!RhPositive && donorRhPositive)
                return false;
            return true;
        }

        // A can receive from A and O
        if (Group == BloodGroup.A)
        {
            if (donorGroup != BloodGroup.A && donorGroup != BloodGroup.O)
                return false;
            if (!RhPositive && donorRhPositive)
                return false;
            return true;
        }

        // B can receive from B and O
        if (Group == BloodGroup.B)
        {
            if (donorGroup != BloodGroup.B && donorGroup != BloodGroup.O)
                return false;
            if (!RhPositive && donorRhPositive)
                return false;
            return true;
        }

        // O can receive from O only
        if (Group == BloodGroup.O)
        {
            if (donorGroup != BloodGroup.O)
                return false;
            if (!RhPositive && donorRhPositive)
                return false;
            return true;
        }

        return false;
    }
}

/// <summary>
/// Blood group enum.
/// </summary>
[Serializable, NetSerializable]
public enum BloodGroup : byte
{
    A,
    B,
    AB,
    O
}
