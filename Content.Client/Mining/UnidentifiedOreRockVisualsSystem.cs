using Content.Shared.Mining;
using Content.Shared.Mining.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client.Mining;

public sealed partial class UnidentifiedOreRockVisualsSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UnidentifiedOreRockComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<UnidentifiedOreRockComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnStartup(Entity<UnidentifiedOreRockComponent> ent, ref ComponentStartup args)
    {
        UpdateSprite(ent);
    }

    private void OnState(Entity<UnidentifiedOreRockComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateSprite(ent);
    }

    private void UpdateSprite(Entity<UnidentifiedOreRockComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent.Owner, out var sprite))
            return;

        if (!_proto.TryIndex(ent.Comp.Ore, out var ore) || ore.OreSprite is not { } oreSprite)
            return;

        _sprite.LayerSetSprite((ent.Owner, sprite), 0, oreSprite);
    }
}
