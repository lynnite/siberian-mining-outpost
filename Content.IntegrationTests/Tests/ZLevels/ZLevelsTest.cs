#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.ZLevels;
using Content.Shared.ZLevels;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.ZLevels;

[TestFixture]
public sealed class ZLevelsTest : GameTest
{
    private static readonly ProtoId<Content.Shared.Maps.ContentTileDefinition> FloorSnow = "FloorSnow";
    private static readonly ProtoId<Content.Shared.Maps.ContentTileDefinition> Space = "Space";

    [Test]
    public async Task NetworkLinksAdjacentLevels()
    {
        EntityUid lower = default;
        EntityUid upper = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var zSys = SEntMan.System<ZLevelSystem>();

            lower = mapSys.CreateMap(out _);
            upper = mapSys.CreateMap(out _);

            var network = zSys.CreateNetwork();
            Assert.That(zSys.TryAddMapToNetwork(network, lower, 0), Is.True);
            Assert.That(zSys.TryAddMapToNetwork(network, upper, 1), Is.True);
        });

        await Server.WaitRunTicks(1);

        await Server.WaitAssertion(() =>
        {
            var zSys = SEntMan.System<ZLevelSystem>();

            Assert.That(zSys.TryMapUp(lower, out var above), Is.True);
            Assert.That(above, Is.EqualTo(upper));

            Assert.That(zSys.TryMapDown(upper, out var below), Is.True);
            Assert.That(below, Is.EqualTo(lower));

            Assert.That(SEntMan.GetComponent<ZLevelComponent>(lower).Depth, Is.EqualTo(0));
            Assert.That(SEntMan.GetComponent<ZLevelComponent>(upper).Depth, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TraversalStopsAtGaps()
    {
        EntityUid level0 = default;
        EntityUid level2 = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var zSys = SEntMan.System<ZLevelSystem>();

            level0 = mapSys.CreateMap(out _);
            level2 = mapSys.CreateMap(out _);

            var network = zSys.CreateNetwork();
            zSys.TryAddMapToNetwork(network, level0, 0);
            zSys.TryAddMapToNetwork(network, level2, 2);
        });

        await Server.WaitRunTicks(1);

        await Server.WaitAssertion(() =>
        {
            var zSys = SEntMan.System<ZLevelSystem>();

            Assert.That(zSys.TryMapUp(level0, out _), Is.False);

            Assert.That(zSys.TryMapDown(level2, out _), Is.False);
        });
    }

    [Test]
    public async Task GetOrCreateFillsGaps()
    {
        EntityUid anchor = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var zSys = SEntMan.System<ZLevelSystem>();

            anchor = mapSys.CreateMap(out _);
            var network = zSys.CreateNetwork();
            zSys.TryAddMapToNetwork(network, anchor, 0);

            var created = zSys.GetOrCreateLevelAtDepthServer(network, 3);
            Assert.That(created, Is.Not.Null);
        });

        await Server.WaitRunTicks(1);

        await Server.WaitAssertion(() =>
        {
            var zSys = SEntMan.System<ZLevelSystem>();

            Assert.That(zSys.TryMapUp(anchor, out var one), Is.True);
            Assert.That(zSys.TryMapUp(one, out var two), Is.True);
            Assert.That(zSys.TryMapUp(two, out var three), Is.True);

            Assert.That(SEntMan.GetComponent<ZLevelComponent>(three).Depth, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task MappingRedirectsPlacements()
    {
        EntityUid baseMap = default;
        EntityUid grid = default;
        EntityUid mapper = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            baseMap = mapSys.CreateMap(out var mapId);
            grid = mapSys.CreateGridEntity(mapId).Owner;

            mapper = SEntMan.SpawnEntity(null, new EntityCoordinates(grid, Vector2.Zero));
        });

        await Server.WaitRunTicks(1);

        EntityUid editing = default;

        await Server.WaitPost(() =>
        {
            var mappingSys = SEntMan.System<ZLevelMappingSystem>();
            mappingSys.SetMapping(mapper, true);

            var mapperEnt = SEntMan.GetComponent<ZLevelMappingComponent>(mapper);
            var attempted = new EntityCoordinates(grid, new Vector2(1f, 1f));

            Assert.That(mappingSys.TryRedirectPlacement((mapper, mapperEnt), attempted, out var redirected), Is.True);

            editing = redirected.EntityId;
            Assert.That(editing, Is.Not.EqualTo(grid));

            var editingDepth = SEntMan.GetComponent<ZLevelComponent>(editing).Depth;
            var baseDepth = SEntMan.GetComponent<ZLevelComponent>(baseMap).Depth;
            Assert.That(editingDepth, Is.EqualTo(baseDepth + 1));
        });

        await Server.WaitRunTicks(1);

        await Server.WaitPost(() =>
        {
            var mappingSys = SEntMan.System<ZLevelMappingSystem>();
            var mapperEnt = SEntMan.GetComponent<ZLevelMappingComponent>(mapper);

            Assert.That(mappingSys.MoveEditingLevel((mapper, mapperEnt), 1), Is.True);

            var newEditing = mapperEnt.EditingLevel;
            Assert.That(newEditing, Is.Not.Null);
            Assert.That(SEntMan.GetComponent<ZLevelComponent>(newEditing!.Value).Depth, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ClimbableGeneratesTopLevel()
    {
        EntityUid grid = default;
        EntityUid obstacle = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var map = mapSys.CreateMap(out var mapId);
            grid = mapSys.CreateGridEntity(mapId).Owner;
        });

        await Server.WaitRunTicks(1);

        await Server.WaitPost(() =>
        {
            obstacle = SEntMan.SpawnEntity(null, new EntityCoordinates(grid, Vector2.Zero));
            SEntMan.System<SharedTransformSystem>().SetParent(obstacle, grid);
            SEntMan.EnsureComponent<ZLevelClimbableComponent>(obstacle);

            SEntMan.System<ZLevelCliffSystem>().EnsureCliffTop(obstacle);
        });

        await Server.WaitRunTicks(1);

        await Server.WaitAssertion(() =>
        {
            var zSys = SEntMan.System<ZLevelSystem>();

            var mapUid = SEntMan.GetComponent<TransformComponent>(grid).MapUid;
            Assert.That(mapUid, Is.Not.Null);

            Assert.That(zSys.TryMapUp(mapUid!.Value, out _), Is.True);
        });
    }

    [Test]
    public async Task ViewerGetsLookUpAction()
    {
        EntityUid viewer = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var map = mapSys.CreateMap(out var mapId);
            mapSys.CreateGridEntity(mapId);

            viewer = SEntMan.SpawnEntity(null, new EntityCoordinates(map, Vector2.Zero));
            SEntMan.EnsureComponent<ZLevelViewerComponent>(viewer);
        });

        await Server.WaitRunTicks(1);

        await Server.WaitAssertion(() =>
        {
            var viewerComp = SEntMan.GetComponent<ZLevelViewerComponent>(viewer);

            Assert.That(viewerComp.LookUp, Is.False);
            Assert.That(viewerComp.ActionEntity, Is.Not.Null);
            Assert.That(SEntMan.EntityExists(viewerComp.ActionEntity!.Value), Is.True);
        });

        await Server.WaitPost(() =>
        {
            var viewerComp = SEntMan.GetComponent<ZLevelViewerComponent>(viewer);
            var actionUid = viewerComp.ActionEntity!.Value;

            var ev = new ZLevelToggleLookUpEvent
            {
                Action = (actionUid, SEntMan.GetComponent<Content.Shared.Actions.Components.ActionComponent>(actionUid)),
            };
            SEntMan.EventBus.RaiseLocalEvent(viewer, ev);
        });

        await Server.WaitRunTicks(1);

        await Server.WaitAssertion(() =>
        {
            var viewerComp = SEntMan.GetComponent<ZLevelViewerComponent>(viewer);
            Assert.That(viewerComp.LookUp, Is.True);
        });
    }

    [Test]
    public async Task DeletingNetworkClearsMapReferences()
    {
        EntityUid map = default;
        EntityUid networkUid = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var zSys = SEntMan.System<ZLevelSystem>();

            map = mapSys.CreateMap(out _);
            var network = zSys.CreateNetwork();
            networkUid = network.Owner;
            zSys.TryAddMapToNetwork(network, map, 0);
        });

        await Server.WaitRunTicks(1);

        await Server.WaitPost(() =>
        {
            var zSys = SEntMan.System<ZLevelSystem>();
            zSys.DeleteNetwork(networkUid);
        });

        await Server.WaitRunTicks(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(networkUid), Is.False);
        });
    }

    [Test]
    public async Task MineMarkerGeneratesStackedOreMine()
    {
        EntityUid grid = default;
        EntityUid marker = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            mapSys.CreateMap(out var mapId);
            grid = mapSys.CreateGridEntity(mapId).Owner;
        });

        await Server.WaitRunTicks(1);

        await Server.WaitPost(() =>
        {
            marker = SEntMan.SpawnEntity("ZLevelMineMarker", new EntityCoordinates(grid, Vector2.Zero));
            SEntMan.System<SharedTransformSystem>().SetParent(marker, grid);
        });

        await Server.WaitRunTicks(2);

        await Server.WaitAssertion(() =>
        {
            var zSys = SEntMan.System<ZLevelSystem>();

            var markerQuery = SEntMan.EntityQueryEnumerator<ZLevelMineMarkerComponent>();
            Assert.That(markerQuery.MoveNext(out _), Is.False, "Mine marker should be deleted after generation.");

            var gridXform = SEntMan.GetComponent<TransformComponent>(grid);
            Assert.That(gridXform.MapUid, Is.Not.Null);
            var anchorMap = gridXform.MapUid!.Value;

            Assert.That(zSys.TryGetMapNetwork(anchorMap, out var network), Is.True);
            Assert.That(network.Comp.ZLevels.Count, Is.GreaterThanOrEqualTo(3));

            Assert.That(network.Comp.ZLevels.ContainsKey(1), Is.False);

            Assert.That(network.Comp.ZLevels.TryGetValue(0, out var topLevel), Is.True);
            Assert.That(topLevel, Is.EqualTo(anchorMap));
            Assert.That(network.Comp.ZLevels.ContainsKey(-1), Is.True);
            Assert.That(network.Comp.ZLevels.ContainsKey(-2), Is.True);

            var wallCount = 0;
            var oreCount = 0;

            var wallQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
            while (wallQuery.MoveNext(out _, out var meta))
            {
                var id = meta.EntityPrototype?.ID;
                if (id == null || !id.StartsWith("WallRockSnow"))
                    continue;

                wallCount++;

                if (id != "WallRockSnow")
                    oreCount++;
            }

            Assert.That(wallCount, Is.GreaterThan(0), "Expected generated snowdrift walls.");
            Assert.That(oreCount, Is.GreaterThan(0), "Expected at least some ore-bearing snowdrift walls.");

            var mapSys = SEntMan.System<SharedMapSystem>();
            var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);

            var markerTile = mapSys.LocalToTile(grid, gridComp, SEntMan.GetComponent<TransformComponent>(grid).Coordinates);
            Assert.That(mapSys.TryGetTileRef(grid, gridComp, markerTile, out var markerTileRef), Is.True);
            Assert.That(markerTileRef.Tile.IsEmpty, Is.True, "Expected the marker tile to be an open shaft.");

            var pavedTile = markerTile + new Vector2i(2, 2);
            Assert.That(mapSys.TryGetTileRef(grid, gridComp, pavedTile, out var pavedTileRef), Is.True);
            Assert.That(pavedTileRef.Tile.IsEmpty, Is.False, "Expected the surface to be paved around the shaft.");
        });
    }

    [Test]
    public async Task MineShaftIsOpenAllTheWayDown()
    {
        EntityUid grid = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            mapSys.CreateMap(out var mapId);
            grid = mapSys.CreateGridEntity(mapId).Owner;
        });

        await Server.WaitRunTicks(1);

        await Server.WaitPost(() =>
        {
            var marker = SEntMan.SpawnEntity("ZLevelMineMarker", new EntityCoordinates(grid, Vector2.Zero));
            SEntMan.System<SharedTransformSystem>().SetParent(marker, grid);
        });

        await Server.WaitRunTicks(2);

        await Server.WaitAssertion(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var zSys = SEntMan.System<ZLevelSystem>();

            var anchorMap = SEntMan.GetComponent<TransformComponent>(grid).MapUid!.Value;
            Assert.That(zSys.TryGetMapNetwork(anchorMap, out var network), Is.True);

            var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);
            var shaftTile = mapSys.LocalToTile(grid, gridComp, SEntMan.GetComponent<TransformComponent>(grid).Coordinates);
            var shaftWorld = mapSys.GridTileToWorldPos(grid, gridComp, shaftTile);

            bool IsShaftFloored(int depth)
            {
                Assert.That(network.Comp.ZLevels.TryGetValue(depth, out var map), Is.True, $"Missing level {depth}.");

                if (!mapSys.TryFindGridAt(map!.Value, shaftWorld, out var levelGrid, out var levelGridComp))
                    return false;

                var indices = mapSys.WorldToTile(levelGrid, levelGridComp, shaftWorld);
                return mapSys.TryGetTileRef(levelGrid, levelGridComp, indices, out var tile) && !tile.Tile.IsEmpty;
            }

            Assert.That(IsShaftFloored(0), Is.False, "Surface shaft should be open to climb/fall in.");

            Assert.That(IsShaftFloored(-1), Is.False, "Intermediate level shaft should stay open to fall through.");

            Assert.That(IsShaftFloored(-2), Is.True, "Deepest level should have a floor to land on.");
        });
    }

    [Test]
    public async Task MineLevelsHaveBreathableAirAndGravity()
    {
        EntityUid grid = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            mapSys.CreateMap(out var mapId);
            grid = mapSys.CreateGridEntity(mapId).Owner;
        });

        await Server.WaitRunTicks(1);

        await Server.WaitPost(() =>
        {
            var marker = SEntMan.SpawnEntity("ZLevelMineMarker", new EntityCoordinates(grid, Vector2.Zero));
            SEntMan.System<SharedTransformSystem>().SetParent(marker, grid);
        });

        await Server.WaitRunTicks(2);

        await Server.WaitAssertion(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var zSys = SEntMan.System<ZLevelSystem>();

            var anchorMap = SEntMan.GetComponent<TransformComponent>(grid).MapUid!.Value;
            Assert.That(zSys.TryGetMapNetwork(anchorMap, out var network), Is.True);

            foreach (var (depth, mapUid) in network.Comp.ZLevels)
            {
                Assert.That(mapUid, Is.Not.Null, $"Level {depth} has no map.");

                Assert.That(SEntMan.HasComponent<Content.Shared.Atmos.Components.MapAtmosphereComponent>(mapUid!.Value),
                    Is.True, $"Level {depth} has no map atmosphere.");
                var atmos = SEntMan.GetComponent<Content.Shared.Atmos.Components.MapAtmosphereComponent>(mapUid.Value);
                Assert.That(atmos.Space, Is.False, $"Level {depth} is flagged as space (vacuum).");

                Assert.That(SEntMan.HasComponent<Content.Shared.Gravity.GravityComponent>(mapUid.Value),
                    Is.True, $"Level {depth} map has no gravity.");
                var mapGravity = SEntMan.GetComponent<Content.Shared.Gravity.GravityComponent>(mapUid.Value);
                Assert.That(mapGravity.Enabled, Is.True, $"Level {depth} map gravity is disabled.");
                Assert.That(mapGravity.Inherent, Is.True, $"Level {depth} map gravity is not inherent.");
            }
        });
    }

    [Test]
    public async Task MineLevelsHaveDiggableFloorsExceptBedrock()
    {
        EntityUid grid = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            mapSys.CreateMap(out var mapId);
            grid = mapSys.CreateGridEntity(mapId).Owner;
        });

        await Server.WaitRunTicks(1);

        await Server.WaitPost(() =>
        {
            var marker = SEntMan.SpawnEntity("ZLevelMineMarker", new EntityCoordinates(grid, Vector2.Zero));
            SEntMan.System<SharedTransformSystem>().SetParent(marker, grid);
        });

        await Server.WaitRunTicks(2);

        await Server.WaitAssertion(() =>
        {
            var zSys = SEntMan.System<ZLevelSystem>();
            var anchorMap = SEntMan.GetComponent<TransformComponent>(grid).MapUid!.Value;
            Assert.That(zSys.TryGetMapNetwork(anchorMap, out var network), Is.True);

            int diggable = 0;
            int bedrock = 0;

            var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out _, out var meta))
            {
                var id = meta.EntityPrototype?.ID;
                if (id == "ZLevelMineFloor")
                    diggable++;
                else if (id == "ZLevelMineFloorBedrock")
                    bedrock++;
            }

            Assert.That(diggable, Is.GreaterThan(0), "Expected dig-able mine floors on the upper levels.");
            Assert.That(bedrock, Is.GreaterThan(0), "Expected bedrock floor on the deepest level.");

            var bedrockQuery = SEntMan.EntityQueryEnumerator<ZLevelDiggableFloorComponent>();
            while (bedrockQuery.MoveNext(out var ent, out var comp))
            {
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID, Is.Not.EqualTo("ZLevelMineFloorBedrock"),
                    "Bedrock must not carry ZLevelDiggableFloorComponent.");
            }
        });
    }

    [Test]
    public async Task EntityFallsDownMineShaft()
    {
        EntityUid grid = default;
        EntityUid anchorMap = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            anchorMap = mapSys.CreateMap(out var mapId);
            grid = mapSys.CreateGridEntity(mapId).Owner;
        });

        await Server.WaitRunTicks(1);

        await Server.WaitPost(() =>
        {
            var marker = SEntMan.SpawnEntity("ZLevelMineMarker", new EntityCoordinates(grid, Vector2.Zero));
            SEntMan.System<SharedTransformSystem>().SetParent(marker, grid);
        });

        await Server.WaitRunTicks(2);

        EntityUid faller = default;

        await Server.WaitPost(() =>
        {
            faller = SEntMan.SpawnEntity(null, new EntityCoordinates(grid, Vector2.Zero));
            SEntMan.System<SharedTransformSystem>().SetParent(faller, grid);
            SEntMan.EnsureComponent<ZLevelPhysicsComponent>(faller);
            var body = SEntMan.EnsureComponent<PhysicsComponent>(faller);
            SEntMan.System<SharedPhysicsSystem>().SetBodyType(faller, BodyType.Dynamic, body: body);

            var zSys = SEntMan.System<ZLevelSystem>();
            zSys.WakeBody(faller);
        });

        await Server.WaitRunTicks(180);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(faller), Is.True);
            var fallerMap = SEntMan.GetComponent<TransformComponent>(faller).MapUid;

            Assert.That(fallerMap, Is.Not.EqualTo(anchorMap),
                "Entity never fell: still on the surface map. Z-level physics likely never ran on it.");

            var zSys = SEntMan.System<ZLevelSystem>();
            Assert.That(zSys.TryGetMapNetwork(fallerMap!.Value, out _), Is.True,
                "Fallen entity is not on a mine level.");

            Assert.That(SEntMan.GetComponent<ZLevelComponent>(fallerMap.Value).Depth, Is.EqualTo(-2),
                "Entity did not fall all the way down the open shaft.");
        });
    }

    [Test]
    public async Task MineWithOffsetAnchorGridFallsOntoLevel()
    {
        EntityUid grid = default;
        EntityUid anchorMap = default;

        var anchorOffset = new Vector2(123.5f + 37f, -58.5f - 11f);

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var xformSys = SEntMan.System<SharedTransformSystem>();

            anchorMap = mapSys.CreateMap(out var mapId);
            grid = mapSys.CreateGridEntity(mapId).Owner;

            xformSys.SetWorldPosition(grid, anchorOffset);
        });

        await Server.WaitRunTicks(1);

        await Server.WaitPost(() =>
        {
            var marker = SEntMan.SpawnEntity("ZLevelMineMarker", new EntityCoordinates(grid, Vector2.Zero));
            SEntMan.System<SharedTransformSystem>().SetParent(marker, grid);
        });

        await Server.WaitRunTicks(2);

        EntityUid faller = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);
            var shaftTile = mapSys.LocalToTile(grid, gridComp, new EntityCoordinates(grid, Vector2.Zero));
            var shaftCenter = mapSys.GridTileToWorld(grid, gridComp, shaftTile);

            faller = SEntMan.SpawnEntity(null, shaftCenter);
            SEntMan.System<SharedTransformSystem>().SetParent(faller, grid);
            SEntMan.EnsureComponent<ZLevelPhysicsComponent>(faller);
            var body = SEntMan.EnsureComponent<PhysicsComponent>(faller);
            SEntMan.System<SharedPhysicsSystem>().SetBodyType(faller, BodyType.Dynamic, body: body);

            var zSys = SEntMan.System<ZLevelSystem>();
            zSys.WakeBody(faller);
        });

        await Server.WaitRunTicks(180);

        await Server.WaitAssertion(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var xformSys = SEntMan.System<SharedTransformSystem>();
            var zSys = SEntMan.System<ZLevelSystem>();

            Assert.That(SEntMan.EntityExists(faller), Is.True);

            var fallerXform = SEntMan.GetComponent<TransformComponent>(faller);
            var fallerMap = fallerXform.MapUid;

            Assert.That(fallerMap, Is.Not.Null);
            Assert.That(fallerMap, Is.Not.EqualTo(anchorMap), "Entity never fell to a lower level.");
            Assert.That(zSys.TryGetMapNetwork(fallerMap!.Value, out _), Is.True, "Entity is not on a mine level.");

            var worldPos = xformSys.GetWorldPosition(fallerXform);

            var foundGrid = false;
            var foundFlooredTile = false;

            var gridQuery = SEntMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (gridQuery.MoveNext(out var levelGridUid, out var levelGrid, out var levelGridXform))
            {
                if (levelGridXform.MapUid != fallerMap.Value)
                    continue;

                var gridWorld = xformSys.GetWorldPosition(levelGridXform);
                if ((gridWorld - anchorOffset).LengthSquared() > 0.001f)
                    continue;

                foundGrid = true;

                var indices = mapSys.WorldToTile(levelGridUid, levelGrid, worldPos);
                if (mapSys.TryGetTileRef(levelGridUid, levelGrid, indices, out var tile) && !tile.Tile.IsEmpty)
                    foundFlooredTile = true;
            }

            Assert.That(foundGrid, Is.True,
                "The level below has no grid aligned with the anchor grid's world position. The mine level " +
                "grids are not aligned with the anchor, so a fallen entity lands off-grid in empty space.");
            Assert.That(foundFlooredTile, Is.True,
                $"Entity landed on an empty tile at depth " +
                $"{SEntMan.GetComponent<ZLevelComponent>(fallerMap.Value).Depth} " +
                $"(world {worldPos}): the level grid is not aligned with the anchor grid's world position " +
                "(the shaft column doesn't line up vertically).");
        });
    }

    [Test]
    public async Task RoofPropagatesThroughOpenTiles()
    {
        EntityUid upperMap = default;
        EntityUid lowerMap = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var zSys = SEntMan.System<ZLevelSystem>();

            lowerMap = mapSys.CreateMap(out var lowerId);
            upperMap = mapSys.CreateMap(out var upperId);
            var lowerGrid = mapSys.CreateGridEntity(lowerId).Owner;
            var upperGrid = mapSys.CreateGridEntity(upperId).Owner;

            var network = zSys.CreateNetwork();
            zSys.TryAddMapToNetwork(network, lowerMap, 0);
            zSys.TryAddMapToNetwork(network, upperMap, 1);

            var floorSnow = SProtoMan.Index(FloorSnow);
            var space = SProtoMan.Index(Space);

            var lowerGridComp = SEntMan.GetComponent<MapGridComponent>(lowerGrid);
            var upperGridComp = SEntMan.GetComponent<MapGridComponent>(upperGrid);

            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 4; y++)
                    mapSys.SetTile(lowerGrid, lowerGridComp, new Vector2i(x, y), new Tile(floorSnow.TileId));
            }

            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 4; y++)
                {
                    var tileId = x == 1 && y == 1 ? space.TileId : floorSnow.TileId;
                    mapSys.SetTile(upperGrid, upperGridComp, new Vector2i(x, y), new Tile(tileId));
                }
            }
        });

        await Server.WaitRunTicks(1);

        await Server.WaitPost(() =>
        {
            SEntMan.System<Content.Server.ZLevels.ZLevelRoofSystem>().RecalculateFrom(upperMap);
        });

        await Server.WaitRunTicks(1);

        await Server.WaitAssertion(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var roof = SEntMan.System<Content.Shared.Light.EntitySystems.SharedRoofSystem>();

            var lowerGridQuery = SEntMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            MapGridComponent? lowerGrid = null;
            EntityUid lowerGridUid = default;
            while (lowerGridQuery.MoveNext(out var gridUid, out var grid, out _))
            {
                if (SEntMan.GetComponent<TransformComponent>(gridUid).MapUid != lowerMap)
                    continue;
                lowerGrid = grid;
                lowerGridUid = gridUid;
                break;
            }

            Assert.That(lowerGrid, Is.Not.Null, "Lower level has no grid.");

            var roofComp = SEntMan.EnsureComponent<Content.Shared.Light.Components.RoofComponent>(lowerGridUid);

            Assert.That(roof.IsRooved((lowerGridUid, lowerGrid!, roofComp), new Vector2i(0, 0)), Is.True,
                "Tile under solid floor above should be roofed.");

            Assert.That(roof.IsRooved((lowerGridUid, lowerGrid!, roofComp), new Vector2i(1, 1)), Is.False,
                "Tile under an open tile above should be roofless so light can reach it.");
        });
    }

    [Test]
    public async Task ClimbableOffersInteractionVerb()
    {
        EntityUid grid = default;
        EntityUid obstacle = default;
        EntityUid user = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            mapSys.CreateMap(out var mapId);
            grid = mapSys.CreateGridEntity(mapId).Owner;

            obstacle = SEntMan.SpawnEntity("WallRockSnow", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            SEntMan.System<SharedTransformSystem>().SetParent(obstacle, grid);

            user = SEntMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            SEntMan.System<SharedTransformSystem>().SetParent(user, grid);
        });

        await Server.WaitRunTicks(1);

        await Server.WaitPost(() =>
        {
            SEntMan.System<ZLevelCliffSystem>().EnsureCliffTop(obstacle);
        });

        await Server.WaitRunTicks(1);

        await Server.WaitAssertion(() =>
        {
            var climb = SEntMan.System<ZLevelClimbSystem>();
            var gridUid = SEntMan.GetComponent<TransformComponent>(user).GridUid;

            Assert.That(gridUid, Is.Not.Null);
            var mapUid = SEntMan.GetComponent<TransformComponent>(gridUid!.Value).MapUid;
            Assert.That(mapUid, Is.Not.Null);

            Assert.That(climb.CanClimb(user, obstacle, mapUid!.Value, 1), Is.True,
                "Climb should be possible when there is a clear ledge above the obstacle and headroom.");

            var args = new Content.Shared.Verbs.GetVerbsEvent<Content.Shared.Verbs.AlternativeVerb>(
                user,
                obstacle,
                @using: null,
                hands: null,
                canInteract: true,
                canComplexInteract: true,
                canAccess: true,
                extraCategories: new List<Content.Shared.Verbs.VerbCategory>());
            SEntMan.EventBus.RaiseLocalEvent(obstacle, args);

            Assert.That(args.Verbs.Any(v => v.Text == "Climb up"), Is.True,
                "Expected a 'Climb up' verb on a climbable obstacle.");
        });
    }

    [Test]
    public async Task DiggingFloorOpensHole()
    {
        EntityUid grid = default;
        EntityUid pickaxe = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            mapSys.CreateMap(out var mapId);
            grid = mapSys.CreateGridEntity(mapId).Owner;
        });

        await Server.WaitRunTicks(1);

        await Server.WaitPost(() =>
        {
            var marker = SEntMan.SpawnEntity("ZLevelMineMarker", new EntityCoordinates(grid, Vector2.Zero));
            SEntMan.System<SharedTransformSystem>().SetParent(marker, grid);
        });

        await Server.WaitRunTicks(2);

        EntityUid floorEnt = default;
        EntityUid floorGrid = default;
        Vector2i dugTile = default;

        await Server.WaitPost(() =>
        {
            var floorQuery = SEntMan.EntityQueryEnumerator<ZLevelDiggableFloorComponent, TransformComponent>();
            while (floorQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid is not { } gridUid || gridUid == grid)
                    continue;

                floorEnt = uid;
                floorGrid = gridUid;
                break;
            }

            Assert.That(floorEnt, Is.Not.EqualTo(default(EntityUid)), "No dig-able floor found to mine.");

            pickaxe = SEntMan.SpawnEntity("Pickaxe", new EntityCoordinates(floorGrid, Vector2.Zero));

            dugTile = SEntMan.System<SharedTransformSystem>().GetGridOrMapTilePosition(floorEnt);
            var gridCompBefore = SEntMan.GetComponent<MapGridComponent>(floorGrid);
            Assert.That(SEntMan.System<SharedMapSystem>().TryGetTileRef(floorGrid, gridCompBefore, dugTile, out var before),
                Is.True);
            Assert.That(before.Tile.IsEmpty, Is.False, "Floor tile should exist before digging.");

            var ev = new Content.Shared.Weapons.Melee.Events.AttackedEvent(pickaxe, pickaxe,
                SEntMan.GetComponent<TransformComponent>(floorEnt).Coordinates);
            SEntMan.EventBus.RaiseLocalEvent(floorEnt, ev);
        });

        await Server.WaitRunTicks(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(floorEnt), Is.False, "Dug floor entity should be gone.");

            var mapSys = SEntMan.System<SharedMapSystem>();
            var gridComp = SEntMan.GetComponent<MapGridComponent>(floorGrid);

            Assert.That(mapSys.TryGetTileRef(floorGrid, gridComp, dugTile, out var after), Is.True);
            Assert.That(after.Tile.IsEmpty, Is.True, "Dug floor tile should be cleared, opening a hole.");
        });
    }

    [Test]
    public async Task DiggingFloorAboveWallBreaksWallBelow()
    {
        EntityUid lowerMap = default;
        EntityUid lowerGrid = default;
        EntityUid upperMap = default;
        EntityUid upperGrid = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var zSys = SEntMan.System<ZLevelSystem>();

            lowerMap = mapSys.CreateMap(out var lowerId);
            lowerGrid = mapSys.CreateGridEntity(lowerId).Owner;
            upperMap = mapSys.CreateMap(out var upperId);
            upperGrid = mapSys.CreateGridEntity(upperId).Owner;

            var network = zSys.CreateNetwork();
            zSys.TryAddMapToNetwork(network, lowerMap, 0);
            zSys.TryAddMapToNetwork(network, upperMap, 1);
        });

        await Server.WaitRunTicks(1);

        var tile = new Vector2i(3, 3);

        EntityUid wallBelow = default;
        EntityUid floorAbove = default;

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();

            var lowerGridComp = SEntMan.GetComponent<MapGridComponent>(lowerGrid);

            var floorTileDef = SProtoMan.Index(FloorSnow);
            mapSys.SetTile(lowerGrid, lowerGridComp, tile, new Tile(floorTileDef.TileId));

            wallBelow = SEntMan.SpawnEntity("WallRockSnow", mapSys.GridTileToLocal(lowerGrid, lowerGridComp, tile));

            var upperGridComp = SEntMan.GetComponent<MapGridComponent>(upperGrid);
            var floorTile = SProtoMan.Index(FloorSnow);
            mapSys.SetTile(upperGrid, upperGridComp, tile, new Tile(floorTile.TileId));
            mapSys.SetTile(upperGrid, upperGridComp, new Vector2i(20, 20), new Tile(floorTile.TileId));

            floorAbove = SEntMan.SpawnEntity("ZLevelMineFloor", mapSys.GridTileToLocal(upperGrid, upperGridComp, tile));

            Assert.That(mapSys.TryGetTileRef(upperGrid, upperGridComp, tile, out var before), Is.True);
            Assert.That(before.Tile.IsEmpty, Is.False, "Floor tile should exist before digging.");

            var zSys = SEntMan.System<ZLevelSystem>();
            Assert.That(zSys.TryMapDown(SEntMan.GetComponent<TransformComponent>(upperGrid).MapUid!.Value, out var below), Is.True);
            Assert.That(below, Is.EqualTo(lowerMap));
        });

        await Server.WaitRunTicks(1);

        await Server.WaitPost(() =>
        {
            var mapSys = SEntMan.System<SharedMapSystem>();
            var upperGridComp = SEntMan.GetComponent<MapGridComponent>(upperGrid);

            SEntMan.System<Content.Shared.Destructible.SharedDestructibleSystem>().DestroyEntity(floorAbove);

            var hasFloor = mapSys.TryGetTileRef(upperGrid, upperGridComp, tile, out var after) && !after.Tile.IsEmpty;
            Assert.That(hasFloor, Is.False, "Digging the floor should clear the tile above and open the shaft.");
        });

        await Server.WaitRunTicks(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(wallBelow), Is.False,
                "Digging the floor should also break the wall directly below.");
        });
    }
}
