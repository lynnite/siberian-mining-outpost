using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Mining.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class UnidentifiedOreRockComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<OrePrototype> Ore = "OreSteel";
}

