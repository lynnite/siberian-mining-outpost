using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Train.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TrainOrderPaperComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId? Product;

    [DataField, AutoNetworkedField]
    public int Quantity;

    [DataField, AutoNetworkedField]
    public bool Fulfilled;
}
