using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Maths;

namespace Content.Shared.Train.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class TrainComponent : Component
{
    [DataField, AutoNetworkedField]
    public TrainState State = TrainState.Docked;

    [DataField]
    public List<TrainRouteNode> Route = new();

    [DataField, AutoNetworkedField]
    public int RouteIndex;

    [DataField, AutoNetworkedField]
    public Vector2 TargetPosition;

    [DataField]
    public int Direction = 1;

    [DataField]
    public float Speed = 2.5f;

    [DataField]
    public float ArriveDistance = 0.1f;

    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextState;

    [DataField]
    public bool PingPong = true;
}

public enum TrainState : byte
{
    Docked,

    Travelling,
}

