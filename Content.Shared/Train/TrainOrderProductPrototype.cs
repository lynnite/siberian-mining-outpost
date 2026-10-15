using Robust.Shared.Prototypes;

namespace Content.Shared.Train;

[Prototype]
public sealed partial class TrainOrderProductPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public EntProtoId Product = default!;

    [DataField(required: true)]
    public List<string> Keywords = new();

    [DataField(required: true)]
    public int Price = 5;
}

