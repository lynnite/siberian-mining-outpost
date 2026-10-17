using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Mining.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RockCompositionComponent : Component
{
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<RockCompositionPrototype>, float> RockContent = new();

    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<OrePrototype>, float> OreContent = new();

    [DataField]
    public EntProtoId? DropPrototype = "UnidentifiedRock";

    [DataField]
    public int MinDrop = 1;

    [DataField]
    public int MaxDrop = 2;

    [DataField]
    public float DropOffset = 0.4f;
}
