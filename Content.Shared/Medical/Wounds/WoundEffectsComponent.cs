using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Wounds;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WoundEffectsComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<WoundEffectInstance> Effects = new();
}

[DataDefinition]
public partial class WoundEffectInstance
{
    [DataField(required: true)]
    public ProtoId<WoundEffectPrototype> Id;

    [DataField]
    public Dictionary<string, float> FloatParams = new();

    [DataField]
    public List<string> StringListParams = new();
}

public static class WoundEffectsHelpers
{
    public static WoundEffectInstance? GetEffect(this WoundEffectsComponent comp, string effectType,
        IPrototypeManager? protoMan = null)
    {
        foreach (var effect in comp.Effects)
        {
            var proto = protoMan?.Index(effect.Id);
            if (proto?.EffectType == effectType)
                return effect;
        }
        return null;
    }

    public static bool HasEffect(this WoundEffectsComponent comp, string effectType,
        IPrototypeManager? protoMan = null)
    {
        return GetEffect(comp, effectType, protoMan) != null;
    }

    public static float GetFloat(this WoundEffectInstance instance, string key,
        float defaultValue = 0)
    {
        if (instance.FloatParams.TryGetValue(key, out var val))
            return val;
        return defaultValue;
    }

    public static void SetFloat(this WoundEffectInstance instance, string key, float value)
    {
        instance.FloatParams[key] = value;
    }

    public static float GetConfigFloat(this WoundEffectInstance instance, string key,
        IPrototypeManager protoMan)
    {
        var proto = protoMan.Index(instance.Id);
        if (proto.Config.TryGetValue(key, out var val))
            return val;
        return 0;
    }

    public static float GetFloatOrConfig(this WoundEffectInstance instance, string key,
        IPrototypeManager protoMan)
    {
        if (instance.FloatParams.TryGetValue(key, out var val))
            return val;
        var proto = protoMan.Index(instance.Id);
        if (proto.Config.TryGetValue(key, out var configVal))
            return configVal;
        return 0;
    }
}
