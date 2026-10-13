using Content.Shared.ZLevels;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.ZLevels;

public sealed partial class ZLevelCliffSystem : EntitySystem
{
    [Dependency] private ZLevelSystem _zLevels = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ITileDefinitionManager _tileDefMan = default!;

    private const string CliffTopTile = "FloorAsteroidSand";

    public bool Suppressed { get; set; }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZLevelClimbableComponent, ComponentStartup>(OnClimbableStartup);
    }

    private void OnClimbableStartup(Entity<ZLevelClimbableComponent> ent, ref ComponentStartup args)
    {
        if (Suppressed)
            return;

        EnsureCliffTop(ent);
    }

    public void EnsureCliffTop(EntityUid obstacle)
    {
        var xform = Transform(obstacle);
        if (xform.MapUid is not { } currentMap)
            return;

        if (!_map.IsInitialized(currentMap) || _map.IsPaused(currentMap))
            return;

        if (xform.GridUid is not { } grid || !HasComp<MapGridComponent>(grid))
            return;

        var network = EnsureNetwork(currentMap);
        var currentDepth = TryComp<ZLevelComponent>(currentMap, out var zMap) ? zMap.Depth : 0;
        var aboveUid = _zLevels.GetOrCreateLevelAtDepthServer(network, currentDepth + 1);
        if (aboveUid is not { } aboveMap)
            return;

        if (!_map.IsInitialized(aboveMap))
            return;

        var worldPos = _transform.GetWorldPosition(obstacle);

        if (!_map.TryFindGridAt(aboveMap, worldPos, out var aboveGrid, out var aboveGridComp))
        {
            var mapId = Comp<MapComponent>(aboveMap).MapId;
            var gridEnt = _map.CreateGridEntity(mapId);
            aboveGrid = gridEnt.Owner;
            aboveGridComp = gridEnt.Comp;
        }

        var indices = _map.WorldToTile(aboveGrid, aboveGridComp, worldPos);

        if (_map.TryGetTileRef(aboveGrid, aboveGridComp, indices, out var tileRef) && !tileRef.Tile.IsEmpty)
            return;

        if (!_tileDefMan.TryGetDefinition(CliffTopTile, out var tileDef))
            return;

        _map.SetTile(aboveGrid, aboveGridComp, indices, new Tile(tileDef.TileId));
    }

    private Entity<ZLevelNetworkComponent> EnsureNetwork(EntityUid mapUid)
    {
        if (_zLevels.TryGetMapNetwork(mapUid, out var network))
            return network;

        var created = _zLevels.CreateNetwork();
        _zLevels.TryAddMapToNetwork(created, mapUid, 0);
        return created;
    }
}
