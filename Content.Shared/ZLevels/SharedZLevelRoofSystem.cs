using System.Diagnostics.CodeAnalysis;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.ZLevels;

public abstract partial class SharedZLevelRoofSystem : EntitySystem
{
    [Dependency] protected SharedZLevelSystem ZLevel = default!;
    [Dependency] protected SharedRoofSystem Roof = default!;
    [Dependency] protected SharedMapSystem Map = default!;
    [Dependency] protected ITileDefinitionManager TileDefMan = default!;

    [Dependency] protected EntityQuery<MapGridComponent> GridQuery = default!;
    [Dependency] protected EntityQuery<ZLevelComponent> ZMapQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZLevelComponent, ComponentStartup>(OnZLevelStartup);
    }

    private void OnZLevelStartup(Entity<ZLevelComponent> ent, ref ComponentStartup args)
    {
        RecalculateFrom(ent.Owner);
    }

    public void RecalculateFrom(EntityUid mapUid)
    {
        if (!ZMapQuery.TryComp(mapUid, out _))
            return;

        if (!TryGetLevelGrid(mapUid, out var mapGridUid, out var mapGrid))
            return;

        var levelsBelow = GetLevelsBelow(mapUid);
        if (levelsBelow.Count == 0)
            return;

        var covered = new Dictionary<Vector2i, bool>();

        var topRoof = EnsureComp<RoofComponent>(mapGridUid);
        foreach (var topTile in Map.GetAllTiles(mapGridUid, mapGrid))
        {
            var tileDef = (ContentTileDefinition)TileDefMan[topTile.Tile.TypeId];
            var roovedHere = Roof.IsRooved((mapGridUid, mapGrid, topRoof), topTile.GridIndices);

            var openHere = tileDef.Transparent && !roovedHere;
            covered[ToWorldTile(mapGridUid, mapGrid, topTile.GridIndices)] = !openHere;
        }

        foreach (var belowUid in levelsBelow)
        {
            if (!TryGetLevelGrid(belowUid, out var belowGridUid, out var belowGrid))
                continue;

            var belowRoof = EnsureComp<RoofComponent>(belowGridUid);

            foreach (var belowTile in Map.GetAllTiles(belowGridUid, belowGrid))
            {
                var worldTile = ToWorldTile(belowGridUid, belowGrid, belowTile.GridIndices);
                var coveredAbove = covered.TryGetValue(worldTile, out var c) && c;
                Roof.SetRoof((belowGridUid, belowGrid, belowRoof), belowTile.GridIndices, coveredAbove);

                var belowTileDef = (ContentTileDefinition)TileDefMan[belowTile.Tile.TypeId];
                covered[worldTile] = coveredAbove || !belowTileDef.Transparent;
            }
        }
    }

    private bool TryGetLevelGrid(EntityUid mapUid, out EntityUid gridUid, [NotNullWhen(true)] out MapGridComponent? grid)
    {
        if (GridQuery.TryComp(mapUid, out grid))
        {
            gridUid = mapUid;
            return true;
        }

        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var gridComp, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            gridUid = uid;
            grid = gridComp;
            return true;
        }

        gridUid = default;
        grid = null;
        return false;
    }

    private Vector2i ToWorldTile(EntityUid mapUid, MapGridComponent grid, Vector2i localTile)
    {
        var world = Map.GridTileToWorldPos(mapUid, grid, localTile);
        return new Vector2i((int)MathF.Floor(world.X), (int)MathF.Floor(world.Y));
    }

    private List<EntityUid> GetLevelsBelow(EntityUid mapUid)
    {
        var result = new List<EntityUid>();

        for (var offset = -1; ZLevel.TryMapOffset(mapUid, offset, out var below); offset--)
            result.Add(below);
        return result;
    }
}



