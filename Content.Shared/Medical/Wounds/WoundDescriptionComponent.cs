using Content.Shared.FixedPoint;

namespace Content.Shared.Medical.Wounds;

[RegisterComponent]
public sealed partial class WoundDescriptionComponent : Component
{
    [DataField(required: true)]
    public SortedDictionary<FixedPoint2, string> Descriptions = new();
}
