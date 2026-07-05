namespace Content.Shared._Medical.Targeting;

public abstract class SharedTargetingSystem : EntitySystem
{
    public static TargetBodyPart[] GetValidParts()
    {
        return new[]
        {
            TargetBodyPart.Head,
            TargetBodyPart.Chest,
            TargetBodyPart.Groin,
            TargetBodyPart.LeftArm,
            TargetBodyPart.LeftHand,
            TargetBodyPart.LeftLeg,
            TargetBodyPart.LeftFoot,
            TargetBodyPart.RightArm,
            TargetBodyPart.RightHand,
            TargetBodyPart.RightLeg,
            TargetBodyPart.RightFoot,
        };
    }
}
