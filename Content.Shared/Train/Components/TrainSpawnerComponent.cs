using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared.Train.Components;

[DataDefinition]
public partial record struct TrainRouteNode
{
    [DataField]
    public Vector2 Position;

    [DataField]
    public float Wait;

    public TrainRouteNode(Vector2 position, float wait)
    {
        Position = position;
        Wait = wait;
    }
}

[DataDefinition]
public partial record struct TrainDelivery
{
    [DataField]
    public EntProtoId Product;

    [DataField]
    public int Quantity;

    public TrainDelivery(EntProtoId product, int quantity)
    {
        Product = product;
        Quantity = quantity;
    }
}

[RegisterComponent, NetworkedComponent]
public sealed partial class TrainSpawnerComponent : Component
{
    [DataField]
    public List<TrainRouteNode> Route = new();

    [DataField]
    public TimeSpan? RespawnAt;

    [DataField]
    public List<TrainDelivery> PendingDeliveries = new();

    [DataField]
    public EntityUid? Owner;
}
