using Robust.Shared.GameStates;

namespace Content.Shared._Medical.Targeting;

[Flags]
public enum TargetBodyPart : ushort
{
    Head = 1,
    Chest = 1 << 1,
    Groin = 1 << 2,
    LeftArm = 1 << 3,
    LeftHand = 1 << 4,
    RightArm = 1 << 5,
    RightHand = 1 << 6,
    LeftLeg = 1 << 7,
    LeftFoot = 1 << 8,
    RightLeg = 1 << 9,
    RightFoot = 1 << 10,

    Arms = LeftArm | RightArm,
    Hands = LeftHand | RightHand,
    Legs = LeftLeg | RightLeg,
    Feet = LeftFoot | RightFoot,
    Torso = Chest | Groin,
    All = Head | Chest | Groin | LeftArm | LeftHand | RightArm | RightHand | LeftLeg | LeftFoot | RightLeg | RightFoot,
}
