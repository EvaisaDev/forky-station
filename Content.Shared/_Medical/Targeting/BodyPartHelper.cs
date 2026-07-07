using System.Linq;

namespace Content.Shared._Medical.Targeting;

public static class BodyPartHelper
{
    private static readonly Dictionary<TargetBodyPart, string> OrganMap = new()
    {
        { TargetBodyPart.Head, "Head" },
        { TargetBodyPart.Torso, "Torso" },
        { TargetBodyPart.LeftArm, "ArmLeft" },
        { TargetBodyPart.LeftHand, "HandLeft" },
        { TargetBodyPart.RightArm, "ArmRight" },
        { TargetBodyPart.RightHand, "HandRight" },
        { TargetBodyPart.LeftLeg, "LegLeft" },
        { TargetBodyPart.LeftFoot, "FootLeft" },
        { TargetBodyPart.RightLeg, "LegRight" },
        { TargetBodyPart.RightFoot, "FootRight" },
    };

    public static string ToOrganCategory(TargetBodyPart part)
        => OrganMap.TryGetValue(part, out var cat) ? cat : "Torso";

    public static TargetBodyPart FromOrganCategory(string category)
        => OrganMap.FirstOrDefault(x => x.Value == category).Key;
}
