using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Surgery;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SurgeryToolComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public string Action = string.Empty;

    [DataField, AutoNetworkedField]
    public float Quality = 1.0f;

    [DataField, AutoNetworkedField]
    public bool Sterile = true;

    [DataField, AutoNetworkedField]
    public float StepDuration = 3.0f;

    [DataField, AutoNetworkedField]
    public List<ProtoId<SurgeryStepPrototype>> Steps = new();
}
