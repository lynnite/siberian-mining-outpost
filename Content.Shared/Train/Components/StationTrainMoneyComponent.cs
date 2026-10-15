using Robust.Shared.GameStates;

namespace Content.Shared.Train.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StationTrainMoneyComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Balance;

    [DataField]
    public bool Initialised;
}
