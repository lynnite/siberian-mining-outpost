using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Train.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class TrainStartMarkerComponent : Component
{
}

[RegisterComponent, NetworkedComponent]
public sealed partial class TrainEndMarkerComponent : Component
{
}

[RegisterComponent, NetworkedComponent]
public sealed partial class TrainStopMarkerComponent : Component
{
    [DataField]
    public float StopDuration = 10f;

    [DataField]
    public LocId? StopName;
}

