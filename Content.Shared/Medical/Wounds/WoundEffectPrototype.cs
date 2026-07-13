using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Medical.Wounds;

[Prototype]
public sealed partial class WoundEffectPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField(required: true)]
    public string EffectType { get; private set; } = string.Empty;

    [DataField]
    public Dictionary<string, float> Config { get; private set; } = new();
}
