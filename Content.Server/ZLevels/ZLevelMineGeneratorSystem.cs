using System.Numerics;
using Content.Shared.Maps;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Content.Shared.ZLevels;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.ZLevels;

public sealed partial class ZLevelMineGeneratorSystem : EntitySystem
{
    [Dependency] private ZLevelSystem _zLevels = default!;
    [Dependency] private ZLevelRoofSystem _zRoofs = default!;
    [Dependency] private ZLevelCliffSystem _cliffs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ITileDefinitionManager _tileDefMan = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private IRobustRandom _random = default!;

    private static readonly ProtoId<ContentTileDefinition> FallbackFloor = "FloorSnow";

    private readonly HashSet<EntityUid> _pending = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZLevelMineMarkerComponent, MapInitEvent>(OnMarkerMapInit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count == 0)
            return;

        foreach (var uid in _pending)
        {
            if (TerminatingOrDeleted(uid) || !TryComp<ZLevelMineMarkerComponent>(uid, out var comp))
                continue;

            GenerateMine((uid, comp));
            QueueDel(uid);
        }

        _pending.Clear();
    }

    private void OnMarkerMapInit(Entity<ZLevelMineMarkerComponent> ent, ref MapInitEvent args)
    {
        _pending.Add(ent);
    }

    public void GenerateMine(Entity<ZLevelMineMarkerComponent> marker)
    {
        var comp = marker.Comp;
        var xform = Transform(marker);

        if (xform.MapUid is not { } anchorMap || xform.GridUid is not { } anchorGrid)
        {
            Log.Warning($"ZLevel mine marker {ToPrettyString(marker)} is not on a grid; skipping generation.");
            return;
        }

        if (comp.Levels <= 0)
            return;

        var worldPos = _transform.GetWorldPosition(xform);
        var seed = comp.Seed != 0
            ? comp.Seed
            : HashCode.Combine((int)worldPos.X, (int)worldPos.Y, marker.Owner.Id);

        _random.SetSeed(seed);
        var random = _random;

        var anchorGridComp = Comp<MapGridComponent>(anchorGrid);
        var anchorTile = _map.LocalToTile(anchorGrid, anchorGridComp, xform.Coordinates);
        var half = new Vector2i(comp.Size.X / 2, comp.Size.Y / 2);
        var origin = anchorTile - half;

        var (anchorGridWorldPos, anchorGridWorldRot) = _transform.GetWorldPositionRotation(anchorGrid);

        var shaft = comp.ShaftRadius < 0
            ? new Box2i(origin, origin + comp.Size - Vector2i.One)
            : new Box2i(anchorTile - new Vector2i(comp.ShaftRadius, comp.ShaftRadius),
                anchorTile + new Vector2i(comp.ShaftRadius, comp.ShaftRadius));

        var network = _zLevels.CreateNetwork();

        var anchorDepth = 0;
        if (_zLevels.TryGetMapNetwork(anchorMap, out var existingNetwork))
        {
            network = existingNetwork;
            if (network.Comp.ZLevelByEntity.TryGetValue(anchorMap, out var existingDepth))
                anchorDepth = existingDepth;
        }
        else
        {
            _zLevels.TryAddMapToNetwork(network, anchorMap, 0);
        }

        var floorTileId = _tileDefMan.TryGetDefinition(comp.FloorTile, out var resolvedFloor)
            ? resolvedFloor.TileId
            : _tileDefMan[FallbackFloor].TileId;

        var oreTable = _protoMan.Index(comp.OreTable);

        var depthByLevel = new int[comp.Levels];
        depthByLevel[0] = anchorDepth;

        var nextDepth = anchorDepth - Math.Max(1, comp.LevelSpacing);
        for (var level = 1; level < comp.Levels; level++)
        {
            while (network.Comp.ZLevels.ContainsKey(nextDepth))
                nextDepth--;

            depthByLevel[level] = nextDepth;
            nextDepth -= Math.Max(1, comp.LevelSpacing);
        }

        var cliffWasSuppressed = _cliffs.Suppressed;
        _cliffs.Suppressed = true;

        try
        {
            var deepest = depthByLevel[comp.Levels - 1];

            for (var level = 0; level < comp.Levels; level++)
            {
                var depth = depthByLevel[level];
                var isSurface = level == 0;
                var floorShaft = depth == deepest;

                if (isSurface)
                {
                    GenerateSurface(anchorGrid, anchorGridComp, origin, shaft, comp, floorTileId, random);
                    continue;
                }

                var levelMap = _zLevels.GetOrCreateLevelAtDepthServer(network, depth);
                if (levelMap is not { } mapUid)
                    continue;

                GenerateLevel(mapUid, origin, shaft, floorShaft, comp, floorTileId, oreTable, random,
                    anchorGridWorldPos, anchorGridWorldRot);
            }
        }
        finally
        {
            _cliffs.Suppressed = cliffWasSuppressed;
        }

        foreach (var (_, mapUid) in network.Comp.ZLevels)
        {
            if (mapUid is { } uid)
                _zLevels.EnsureLevelEnvironment(uid, comp.AmbientLight);
        }

        foreach (var (_, mapUid) in network.Comp.ZLevels)
        {
            if (mapUid is { } uid)
                _zRoofs.RecalculateFrom(uid);
        }
    }

    private void GenerateSurface(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i origin,
        Box2i shaft,
        ZLevelMineMarkerComponent comp,
        ushort floorTileId,
        IRobustRandom random)
    {
        for (var x = 0; x < comp.Size.X; x++)
        {
            for (var y = 0; y < comp.Size.Y; y++)
            {
                var index = origin + new Vector2i(x, y);

                if (shaft.Contains(index))
                    continue;

                if (_map.TryGetTileRef(gridUid, grid, index, out var existing) && !existing.Tile.IsEmpty)
                    continue;

                _map.SetTile(gridUid, grid, index, new Tile(floorTileId));

                if (random.NextFloat() > comp.SurfaceOpenFraction)
                    SpawnRock(gridUid, grid, index, comp, null, random, allowOre: false);
            }
        }
    }

    private void GenerateLevel(
        EntityUid mapUid,
        Vector2i origin,
        Box2i shaft,
        bool floorShaft,
        ZLevelMineMarkerComponent comp,
        ushort floorTileId,
        WeightedRandomEntityPrototype oreTable,
        IRobustRandom random,
        Vector2 anchorGridWorldPos,
        Angle anchorGridWorldRot)
    {
        if (!TryComp<MapComponent>(mapUid, out var mapComp))
            return;

        var gridEnt = _map.CreateGridEntity(mapComp.MapId);
        var grid = gridEnt.Comp;

        _transform.SetWorldPositionRotation(gridEnt.Owner, anchorGridWorldPos, anchorGridWorldRot);

        var filled = new bool[comp.Size.X, comp.Size.Y];
        for (var x = 0; x < comp.Size.X; x++)
        {
            for (var y = 0; y < comp.Size.Y; y++)
                filled[x, y] = true;
        }

        CarveTunnels(comp, shaft, origin, filled, random);

        for (var x = 0; x < comp.Size.X; x++)
        {
            for (var y = 0; y < comp.Size.Y; y++)
            {
                if (shaft.Contains(origin + new Vector2i(x, y)))
                    filled[x, y] = false;
            }
        }

        var tiles = new List<(Vector2i Index, Tile Tile)>(comp.Size.X * comp.Size.Y);
        for (var x = 0; x < comp.Size.X; x++)
        {
            for (var y = 0; y < comp.Size.Y; y++)
            {
                var index = origin + new Vector2i(x, y);

                if (shaft.Contains(index) && !floorShaft)
                    continue;

                tiles.Add((index, new Tile(floorTileId)));
            }
        }

        _map.SetTiles(gridEnt.Owner, grid, tiles);

        var floorProto = floorShaft ? comp.BedrockFloorPrototype : comp.DiggableFloorPrototype;
        if (floorProto is { } floorEntity)
        {
            foreach (var (index, _) in tiles)
            {
                if (shaft.Contains(index) && !floorShaft)
                    continue;

                Spawn(floorEntity, _map.GridTileToLocal(gridEnt.Owner, grid, index));
            }
        }

        for (var x = 0; x < comp.Size.X; x++)
        {
            for (var y = 0; y < comp.Size.Y; y++)
            {
                if (!filled[x, y])
                    continue;

                var index = origin + new Vector2i(x, y);
                SpawnRock(gridEnt.Owner, grid, index, comp, oreTable, random, allowOre: true);
            }
        }

        StampOreVeins(gridEnt.Owner, grid, origin, shaft, comp, filled, oreTable, random);

        BorderMine(gridEnt.Owner, grid, origin, comp, oreTable, random, allowOre: true);
    }

    private void CarveTunnels(
        ZLevelMineMarkerComponent comp,
        Box2i shaft,
        Vector2i origin,
        bool[,] filled,
        IRobustRandom random)
    {
        const int rim = 1;

        var minX = rim;
        var minY = rim;
        var maxX = comp.Size.X - 1 - rim;
        var maxY = comp.Size.Y - 1 - rim;

        if (maxX <= minX || maxY <= minY)
            return;

        var shaftCenter = shaft.Center;
        var startX = Math.Clamp((int)shaftCenter.X - origin.X, minX, maxX);
        var startY = Math.Clamp((int)shaftCenter.Y - origin.Y, minY, maxY);

        var tunnelCount = Math.Max(1, comp.TunnelCount);
        var totalCells = (maxX - minX + 1) * (maxY - minY + 1);
        var targetOpen = Math.Clamp(comp.OpenFraction, 0.02f, 0.9f) * totalCells;

        var walkers = new List<(Vector2i Pos, int Dir, int Remaining)>(tunnelCount);
        for (var i = 0; i < tunnelCount; i++)
        {
            var length = random.Next(comp.TunnelLength.X, comp.TunnelLength.Y + 1);
            walkers.Add((new Vector2i(startX, startY), random.Next(4), length));
        }

        var openCells = 0;
        var guard = 0;
        var guardLimit = totalCells * 8;

        while (openCells < targetOpen && walkers.Count > 0 && guard++ < guardLimit)
        {
            for (var i = walkers.Count - 1; i >= 0; i--)
            {
                var (pos, dir, remaining) = walkers[i];

                if (remaining <= 0)
                {
                    walkers.RemoveAt(i);
                    continue;
                }

                if (random.Prob(comp.TunnelTurnChance))
                    dir = random.Next(4);

                var delta = dir switch
                {
                    0 => new Vector2i(1, 0),
                    1 => new Vector2i(-1, 0),
                    2 => new Vector2i(0, 1),
                    _ => new Vector2i(0, -1),
                };

                var next = pos + delta;

                if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY)
                {
                    dir = random.Next(4);
                    walkers[i] = (pos, dir, remaining - 1);
                    continue;
                }

                if (filled[next.X, next.Y])
                {
                    filled[next.X, next.Y] = false;
                    openCells++;
                }

                walkers[i] = (next, dir, remaining - 1);
            }
        }
    }

    private void SpawnRock(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i index,
        ZLevelMineMarkerComponent comp,
        WeightedRandomEntityPrototype? oreTable,
        IRobustRandom random,
        bool allowOre)
    {
        string proto = comp.WallPrototype;

        if (allowOre && oreTable != null && random.Prob(comp.OreChance))
            proto = oreTable.Pick(random);

        Spawn(proto, _map.GridTileToLocal(gridUid, grid, index));
    }

    private void StampOreVeins(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i origin,
        Box2i shaft,
        ZLevelMineMarkerComponent comp,
        bool[,] filled,
        WeightedRandomEntityPrototype oreTable,
        IRobustRandom random)
    {
        for (var vein = 0; vein < comp.VeinsPerLevel; vein++)
        {
            string proto = oreTable.Pick(random);

            var startX = random.Next(1, Math.Max(2, comp.Size.X - 1));
            var startY = random.Next(1, Math.Max(2, comp.Size.Y - 1));
            var size = random.Next(comp.VeinSize.X, comp.VeinSize.Y + 1);

            var frontier = new List<Vector2i> { new(startX, startY) };

            while (frontier.Count > 0 && size > 0)
            {
                var pick = random.Next(frontier.Count);
                var cell = frontier[pick];
                frontier.RemoveAt(pick);

                if (cell.X < 1 || cell.Y < 1 || cell.X >= comp.Size.X - 1 || cell.Y >= comp.Size.Y - 1)
                    continue;

                if (shaft.Contains(origin + cell))
                    continue;

                if (!filled[cell.X, cell.Y])
                    continue;

                var index = origin + cell;

                foreach (var existing in _map.GetAnchoredEntities((gridUid, grid), index))
                    QueueDel(existing);

                Spawn(proto, _map.GridTileToLocal(gridUid, grid, index));

                size--;

                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;

                        frontier.Add(new Vector2i(cell.X + dx, cell.Y + dy));
                    }
                }
            }
        }
    }

    private void BorderMine(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i origin,
        ZLevelMineMarkerComponent comp,
        WeightedRandomEntityPrototype oreTable,
        IRobustRandom random,
        bool allowOre)
    {
        for (var x = 0; x < comp.Size.X; x++)
        {
            EnsureBorderTile(gridUid, grid, origin + new Vector2i(x, 0), comp, oreTable, random, allowOre);
            EnsureBorderTile(gridUid, grid, origin + new Vector2i(x, comp.Size.Y - 1), comp, oreTable, random, allowOre);
        }

        for (var y = 0; y < comp.Size.Y; y++)
        {
            EnsureBorderTile(gridUid, grid, origin + new Vector2i(0, y), comp, oreTable, random, allowOre);
            EnsureBorderTile(gridUid, grid, origin + new Vector2i(comp.Size.X - 1, y), comp, oreTable, random, allowOre);
        }
    }

    private void EnsureBorderTile(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i index,
        ZLevelMineMarkerComponent comp,
        WeightedRandomEntityPrototype oreTable,
        IRobustRandom random,
        bool allowOre)
    {
        foreach (var existing in _map.GetAnchoredEntities((gridUid, grid), index))
        {
            if (HasComp<ZLevelClimbableComponent>(existing))
                return;
        }

        SpawnRock(gridUid, grid, index, comp, oreTable, random, allowOre);
    }
}
