using Content.Shared.Destructible;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.ZLevels;

public sealed partial class ZLevelDiggableFloorSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedZLevelSystem _zLevels = default!;
    [Dependency] private SharedDestructibleSystem _destructible = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZLevelDiggableFloorComponent, DestructionEventArgs>(OnFloorDestroyed);
    }

    private void OnFloorDestroyed(Entity<ZLevelDiggableFloorComponent> ent, ref DestructionEventArgs args)
    {
        if (!ent.Comp.RemoveTile)
            return;

        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var tile = _transform.GetGridOrMapTilePosition(ent);

        if (TryGetWallBelow(gridUid, grid, tile, out var wallBelow))
            DestroyWallBelow(wallBelow);

        OpenHole(gridUid, grid, tile);
    }

    private void DestroyWallBelow(EntityUid wall)
    {
        if (!Exists(wall))
            return;

        _destructible.DestroyEntity(wall);
    }

    private void OpenHole(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        _map.SetTile(gridUid, grid, tile, Tile.Empty);
    }

    private bool TryGetWallBelow(EntityUid gridUid, MapGridComponent grid, Vector2i tile, out EntityUid wall)
    {
        wall = EntityUid.Invalid;

        var worldPos = _map.GridTileToWorld(gridUid, grid, tile).Position;

        if (Transform(gridUid).MapUid is not { } mapUid)
            return false;

        if (!_zLevels.TryMapDown(mapUid, out var mapBelow))
            return false;

        if (!_map.TryFindGridAt(mapBelow, worldPos, out var belowGridUid, out var belowGrid))
            return false;

        var belowTile = _map.WorldToTile(belowGridUid, belowGrid, worldPos);

        var query = _map.GetAnchoredEntities(belowGridUid, belowGrid, belowTile);
        while (query.MoveNext(out var uid))
        {
            if (uid is null)
                continue;

            if (HasComp<ZLevelMineFloorComponent>(uid.Value) || HasComp<ZLevelDiggableFloorComponent>(uid.Value))
                continue;

            wall = uid.Value;
            return true;
        }

        return false;
    }
}
