using Robust.Shared.Prototypes;

namespace Content.Shared.Mining;

[Prototype]
public sealed partial class RockDistributionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public Dictionary<ProtoId<RockCompositionPrototype>, float> Rocks { get; private set; } = new();
}
