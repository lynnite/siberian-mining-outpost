using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Mining;

[Prototype]
public sealed partial class RockCompositionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public LocId Name = string.Empty;

    [DataField]
    public float Prevalence = 1f;

    [DataField]
    public SpriteSpecifier Sprite = new SpriteSpecifier.Rsi(new("/Textures/Objects/Materials/ore.rsi"), "rock");
}
