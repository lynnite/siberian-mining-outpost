using Robust.Shared.GameStates;

namespace Content.Shared.Train.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class TrainMoneyCounterComponent : Component
{
    [DataField]
    public EntityUid? Station;

    [DataField]
    public float PopupCooldown = 0.5f;
}
