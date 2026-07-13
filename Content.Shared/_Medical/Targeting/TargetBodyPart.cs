using Robust.Shared.GameStates;

namespace Content.Shared._Medical.Targeting;

[Flags]
public enum TargetBodyPart : ushort
{
    Head = 1,
    Torso = 1 << 1,
    LeftArm = 1 << 2,
    LeftHand = 1 << 3,
    RightArm = 1 << 4,
    RightHand = 1 << 5,
    LeftLeg = 1 << 6,
    LeftFoot = 1 << 7,
    RightLeg = 1 << 8,
    RightFoot = 1 << 9,

    Arms = LeftArm | RightArm,
    Hands = LeftHand | RightHand,
    Legs = LeftLeg | RightLeg,
    Feet = LeftFoot | RightFoot,
    All = Head | Torso | LeftArm | LeftHand | RightArm | RightHand | LeftLeg | LeftFoot | RightLeg | RightFoot,
}
