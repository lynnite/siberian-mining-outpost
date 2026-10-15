using System.Collections.Generic;
using System.Numerics;
using Content.Server.Cargo.Components;
using Content.Server.Station.Systems;
using Content.Server.Train;
using Content.Shared.CCVar;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Paper;
using Content.Shared.Train.Components;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests.Train;

[TestFixture]
public sealed class TrainDeliveryTest : GameTest
{
    [Test]
    public async Task ParseOrderSteel()
    {
        await Pair.CreateTestMap();

        var orderSystem = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainOrderSystem>();

        await Server.WaitAssertion(() =>
        {
            Assert.That(orderSystem.TryParseOrder("Steel sheet 1x", out var product, out var qty), Is.True);
            Assert.That(product.Id, Is.EqualTo("SheetSteel1"));
            Assert.That(qty, Is.EqualTo(1));

            Assert.That(orderSystem.TryParseOrder("steel sheets 3x", out product, out qty), Is.True);
            Assert.That(product.Id, Is.EqualTo("SheetSteel1"));
            Assert.That(qty, Is.EqualTo(3));

            Assert.That(orderSystem.TryParseOrder("metal sheet 200x", out product, out qty), Is.True);
            Assert.That(qty, Is.EqualTo(200));
        });
    }

    [Test]
    public async Task ParseOrdersMultipleLines()
    {
        await Pair.CreateTestMap();

        var orderSystem = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainOrderSystem>();

        await Server.WaitAssertion(() =>
        {
            var orders = orderSystem.ParseOrders("steel sheet 1x\nsteel sheet 2x\nsteel sheet 3x");

            Assert.That(orders.Count, Is.EqualTo(3), "Each non-empty line should produce its own order.");
            Assert.That(orders[0].Product.Id, Is.EqualTo("SheetSteel1"));
            Assert.That(orders[0].Quantity, Is.EqualTo(1));
            Assert.That(orders[1].Quantity, Is.EqualTo(2));
            Assert.That(orders[2].Quantity, Is.EqualTo(3));

            var sparse = orderSystem.ParseOrders("\nsteel sheet 2x\n\nbananas 1x\n");
            Assert.That(sparse.Count, Is.EqualTo(1));
            Assert.That(sparse[0].Quantity, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ParseOrderInvalid()
    {
        await Pair.CreateTestMap();

        var orderSystem = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainOrderSystem>();

        await Server.WaitAssertion(() =>
        {
            Assert.That(orderSystem.TryParseOrder("bananas 5x", out _, out _), Is.False);
            Assert.That(orderSystem.TryParseOrder("", out _, out _), Is.False);
        });
    }

    [Test]
    public async Task MoneySpending()
    {
        await Pair.CreateTestMap();

        var cfg = Server.ResolveDependency<IConfigurationManager>();
        var money = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainMoneySystem>();

        await Server.WaitAssertion(() =>
        {
            var station = SSpawnAtPosition("SheetSteel1", Pair.TestMap!.GridCoords);
            var comp = money.EnsureMoney(station);

            Assert.That(cfg.GetCVar(CCVars.TrainStartingMoney), Is.EqualTo(100),
                "The train starting money CVar should default to 100.");

            Assert.That(comp.Balance, Is.EqualTo(100));

            Assert.That(money.TrySpend(station, comp.Balance + 1), Is.False);

            Assert.That(money.TrySpend(station, 40), Is.True);
            Assert.That(money.GetBalance(station), Is.EqualTo(60));
        });
    }

    [Test]
    public async Task TrainTravelsBetweenMarkers()
    {
        var testMap = await Pair.CreateTestMap();
        var grid = testMap.Grid;
        var gridUid = grid.Owner;

        await Server.WaitPost(() =>
        {
            SSpawnAtPosition("TrainStartMarker", new EntityCoordinates(gridUid, new Vector2(0, 0)));
            SSpawnAtPosition("TrainEndMarker", new EntityCoordinates(gridUid, new Vector2(4, 0)));
        });

        await Server.WaitRunTicks(1);

        var trainSys = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainSystem>();
        var transform = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<SharedTransformSystem>();

        await Server.WaitPost(() => trainSys.TrySetupTrain());
        await Server.WaitRunTicks(1);

        var startPos = Vector2.Zero;
        var endPos = Vector2.Zero;

        await Server.WaitAssertion(() =>
        {
            Assert.That(trainSys.TrainExists(), Is.True);

            var q = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
            q.MoveNext(out _, out var tc);
            startPos = tc.Route[0].Position;
            endPos = tc.Route[^1].Position;
        });

        await Server.WaitPost(() =>
        {
            var q = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
            while (q.MoveNext(out _, out var tc))
            {
                tc.Speed = 2.5f;
                tc.NextState = TimeSpan.Zero;
            }
        });

        await Server.WaitRunTicks(30);

        await Server.WaitAssertion(() =>
        {
            Assert.That(trainSys.TrainExists(), Is.True);

            var q = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
            q.MoveNext(out var uid, out _);

            var pos = transform.GetWorldPosition(uid);
            var distStart = (pos - startPos).Length();
            var distEnd = (pos - endPos).Length();

            Assert.That(distStart, Is.GreaterThan(0.5f),
                "The train should have departed the start marker and begun travelling.");
            Assert.That(distEnd, Is.LessThan((endPos - startPos).Length()),
                "The train should be moving toward the end marker.");
        });
    }

    [Test]
    public async Task TrainSetupLoadsAndDoesNotCollide()
    {
        var testMap = await Pair.CreateTestMap();
        var grid = testMap.Grid;
        var gridUid = grid.Owner;

        await Server.WaitPost(() =>
        {
            SSpawnAtPosition("TrainStartMarker", new EntityCoordinates(gridUid, new Vector2(0, 0)));
            SSpawnAtPosition("TrainEndMarker", new EntityCoordinates(gridUid, new Vector2(5, 0)));
        });

        await Server.WaitRunTicks(1);

        var trainSys = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainSystem>();

        await Server.WaitPost(() => trainSys.TrySetupTrain());

        await Server.WaitRunTicks(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(trainSys.TrainExists(), Is.True,
                "The train should have been loaded from the start marker.");

            var trainUid = EntityUid.Invalid;
            var query = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                trainUid = uid;
                break;
            }

            Assert.That(trainUid.IsValid(), Is.True);

            var xform = Server.EntMan.GetComponent<TransformComponent>(trainUid);
            var children = xform.ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (!Server.EntMan.TryGetComponent<FixturesComponent>(child, out var childFixtures))
                    continue;

                foreach (var (_, fixture) in childFixtures.Fixtures)
                {
                    Assert.That(fixture.CollisionLayer, Is.EqualTo(0),
                        $"{Server.EntMan.ToPrettyString(child)} on the train should have a zero collision layer.");
                    Assert.That(fixture.CollisionMask, Is.EqualTo(0),
                        $"{Server.EntMan.ToPrettyString(child)} on the train should have a zero collision mask.");
                }
            }
        });
    }

    [Test]
    public async Task CargoRidesTrainOverOverlappingGrid()
    {
        var testMap = await Pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        var mapSystem = Server.System<SharedMapSystem>();
        var transform = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<SharedTransformSystem>();

        EntityUid stationGrid = default;
        await Server.WaitPost(() =>
        {
            stationGrid = mapSystem.CreateGridEntity(testMap.MapId).Owner;
        });

        await Server.WaitRunTicks(2);

        await Server.WaitPost(() =>
        {
            var stationGridComp = Server.EntMan.GetComponent<MapGridComponent>(stationGrid);
            var ent = new Entity<MapGridComponent>(stationGrid, stationGridComp);

            for (var x = 0; x < 12; x++)
            {
                for (var y = -2; y <= 2; y++)
                    mapSystem.SetTile(ent, new Vector2i(x, y), new Tile(1));
            }

            transform.SetWorldPosition(stationGrid, new Vector2(5, 0));
        });

        await Server.WaitRunTicks(2);

        await Server.WaitPost(() =>
        {
            SSpawnAtPosition("TrainStartMarker", new EntityCoordinates(gridUid, new Vector2(0, 0)));
            SSpawnAtPosition("TrainEndMarker", new EntityCoordinates(gridUid, new Vector2(500, 0)));
        });

        await Server.WaitRunTicks(1);

        var trainSys = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainSystem>();
        await Server.WaitPost(() => trainSys.TrySetupTrain());
        await Server.WaitRunTicks(2);

        EntityUid trainUid = default;
        EntityUid palletUid = default;
        await Server.WaitAssertion(() =>
        {
            var q = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
            q.MoveNext(out trainUid, out _);

            var pq = Server.EntMan.EntityQueryEnumerator<CargoPalletComponent, TransformComponent>();
            while (pq.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == trainUid)
                {
                    palletUid = uid;
                    break;
                }
            }
        });

        Assert.That(trainUid.IsValid(), Is.True, "The train should have spawned.");
        Assert.That(palletUid.IsValid(), Is.True, "The train should have at least one cargo pallet.");

        EntityUid item = default;
        await Server.WaitPost(() =>
        {
            var palletCoords = Server.EntMan.GetComponent<TransformComponent>(palletUid).Coordinates;
            item = SSpawnAtPosition("SheetSteel1", palletCoords);

            var q = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
            q.MoveNext(out _, out var tc);
            tc.Speed = 20f;
            tc.NextState = TimeSpan.Zero;
        });

        await Server.WaitRunTicks(2);

        var trainStart = Vector2.Zero;
        var itemStart = Vector2.Zero;
        var stationStart = Vector2.Zero;
        await Server.WaitAssertion(() =>
        {
            var itemXform = Server.EntMan.GetComponent<TransformComponent>(item);
            trainStart = transform.GetWorldPosition(trainUid);
            itemStart = transform.GetWorldPosition(item);
            stationStart = transform.GetWorldPosition(stationGrid);

            Assert.That(itemXform.GridUid, Is.EqualTo(trainUid),
                "Cargo placed on the train should be parented to the train grid.");
        });

        await Server.WaitRunTicks(60);

        await Server.WaitAssertion(() =>
        {
            var trainNow = transform.GetWorldPosition(trainUid);
            var itemNow = transform.GetWorldPosition(item);
            var trainMoved = (trainNow - trainStart).Length();
            var itemMoved = (itemNow - itemStart).Length();

            Assert.That(trainMoved, Is.GreaterThan(5f),
                "The train should have travelled across the overlapping grid during the test.");

            var itemXform = Server.EntMan.GetComponent<TransformComponent>(item);
            Assert.That(itemXform.GridUid, Is.EqualTo(trainUid),
                "Cargo should still be riding the train after crossing the other grid.");
            Assert.That(itemMoved, Is.EqualTo(trainMoved).Within(0.5f),
                "Cargo should move with the train, not be left on the grid it drove over.");

            var stationNow = transform.GetWorldPosition(stationGrid);
            Assert.That((stationNow - stationStart).Length(), Is.LessThan(0.5f),
                "The train should pass over other grids without moving them.");
        });
    }

    [Test]
    public async Task StressTrainOverLargeGrid()
    {
        var testMap = await Pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        var mapSystem = Server.System<SharedMapSystem>();
        var transform = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<SharedTransformSystem>();

        EntityUid stationGrid = default;
        await Server.WaitPost(() => stationGrid = mapSystem.CreateGridEntity(testMap.MapId).Owner);
        await Server.WaitRunTicks(2);

        await Server.WaitPost(() =>
        {
            var comp = Server.EntMan.GetComponent<MapGridComponent>(stationGrid);
            var ent = new Entity<MapGridComponent>(stationGrid, comp);
            for (var x = -20; x < 40; x++)
            for (var y = -20; y < 20; y++)
                mapSystem.SetTile(ent, new Vector2i(x, y), new Tile(1));

            transform.SetWorldPosition(stationGrid, new Vector2(15, 0));
        });

        await Server.WaitRunTicks(2);

        await Server.WaitPost(() =>
        {
            SSpawnAtPosition("TrainStartMarker", new EntityCoordinates(gridUid, new Vector2(0, 0)));
            SSpawnAtPosition("TrainEndMarker", new EntityCoordinates(gridUid, new Vector2(5000, 0)));
        });

        await Server.WaitRunTicks(1);

        var trainSys = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainSystem>();
        await Server.WaitPost(() => trainSys.TrySetupTrain());
        await Server.WaitRunTicks(2);

        await Server.WaitPost(() =>
        {
            for (var i = 0; i < 120; i++)
                SSpawnAtPosition("SheetSteel1", new EntityCoordinates(stationGrid, new Vector2(i % 50, i / 50)));

            var q = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
            q.MoveNext(out _, out var tc);
            tc.Speed = 40f;
            tc.NextState = TimeSpan.Zero;
        });

        for (var i = 0; i < 400; i++)
        {
            await Server.WaitRunTicks(1);
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(trainSys.TrainExists(), Is.True);
        });
    }

    [Test]
    public async Task CargoCaptureLeavesHeldItemsAlone()
    {
        var testMap = await Pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        var transform = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<SharedTransformSystem>();

        await Server.WaitPost(() =>
        {
            SSpawnAtPosition("TrainStartMarker", new EntityCoordinates(gridUid, new Vector2(0, 0)));
            SSpawnAtPosition("TrainEndMarker", new EntityCoordinates(gridUid, new Vector2(500, 0)));
        });

        await Server.WaitRunTicks(1);

        var trainSys = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainSystem>();
        await Server.WaitPost(() => trainSys.TrySetupTrain());
        await Server.WaitRunTicks(2);

        EntityUid trainUid = default;
        EntityUid palletUid = default;
        await Server.WaitAssertion(() =>
        {
            var q = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
            q.MoveNext(out trainUid, out _);

            var pq = Server.EntMan.EntityQueryEnumerator<CargoPalletComponent, TransformComponent>();
            while (pq.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == trainUid)
                {
                    palletUid = uid;
                    break;
                }
            }
        });

        Assert.That(trainUid.IsValid(), Is.True);
        Assert.That(palletUid.IsValid(), Is.True);

        EntityUid human = default;
        EntityUid held = default;
        await Server.WaitPost(() =>
        {
            var hands = Server.System<SharedHandsSystem>();
            var palletCoords = Server.EntMan.GetComponent<TransformComponent>(palletUid).Coordinates;

            human = SSpawnAtPosition("MobHuman", palletCoords);
            held = SSpawnAtPosition("SheetSteel1", palletCoords);
            hands.TryPickupAnyHand(human, held);
        });

        await Server.WaitRunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var hands = Server.System<SharedHandsSystem>();
            var heldXform = Server.EntMan.GetComponent<TransformComponent>(held);

            Assert.That(hands.IsHolding(human, held), Is.True,
                "An item held by an entity on the train must not be captured as cargo.");
            Assert.That(heldXform.ParentUid, Is.EqualTo(human),
                "A held item should remain parented to its holder, not to the train grid.");
        });
    }

    [Test]
    public async Task TrainFacesEndMarkerOnSpawn()
    {
        var testMap = await Pair.CreateTestMap();
        var grid = testMap.Grid;
        var gridUid = grid.Owner;

        await Server.WaitPost(() =>
        {
            SSpawnAtPosition("TrainStartMarker", new EntityCoordinates(gridUid, new Vector2(0, 0)));
            SSpawnAtPosition("TrainEndMarker", new EntityCoordinates(gridUid, new Vector2(10, 0)));
        });

        await Server.WaitRunTicks(1);

        var trainSys = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainSystem>();
        var cfg = Server.ResolveDependency<IConfigurationManager>();
        await Server.WaitPost(() => trainSys.TrySetupTrain());
        await Server.WaitRunTicks(2);

        var transform = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<SharedTransformSystem>();

        await Server.WaitAssertion(() =>
        {
            var trainUid = EntityUid.Invalid;
            Vector2 routeStart = default;
            Vector2 routeEnd = default;

            var query = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
            while (query.MoveNext(out var uid, out var tc))
            {
                trainUid = uid;
                routeStart = tc.Route[0].Position;
                routeEnd = tc.Route[^1].Position;
                break;
            }

            Assert.That(trainUid.IsValid(), Is.True);

            var toEnd = routeEnd - routeStart;
            var offset = cfg.GetCVar(CCVars.TrainSpawnRotation);
            var expected = (toEnd.ToWorldAngle() + Angle.FromDegrees(offset)).Reduced();
            var actual = transform.GetWorldRotation(trainUid).Reduced();

            Assert.That(
                Math.Abs(Angle.ShortestDistance(expected, actual).Degrees), Is.LessThan(0.5),
                $"The train should face the end marker. Expected {expected.Degrees} degrees but was {actual.Degrees}.");
        });
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.TrainLoop), true)]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.TrainDepartureCooldown), 3f)]
    public async Task TrainDespawnsAtEndAndRespawns()
    {
        var testMap = await Pair.CreateTestMap();
        var grid = testMap.Grid;
        var gridUid = grid.Owner;

        await Server.WaitPost(() =>
        {
            SSpawnAtPosition("TrainStartMarker", new EntityCoordinates(gridUid, new Vector2(0, 0)));
            SSpawnAtPosition("TrainEndMarker", new EntityCoordinates(gridUid, new Vector2(2, 0)));
        });

        await Server.WaitRunTicks(1);

        var cfg = Server.ResolveDependency<IConfigurationManager>();
        var trainSys = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainSystem>();

        await Server.WaitPost(() => trainSys.TrySetupTrain());

        await Server.WaitAssertion(() =>
        {
            Assert.That(cfg.GetCVar(CCVars.TrainDepartureCooldown), Is.EqualTo(3f));
            Assert.That(cfg.GetCVar(CCVars.TrainLoop), Is.True);
        });

        await Server.WaitPost(() =>
        {
            var q = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
            while (q.MoveNext(out _, out var tc))
            {
                tc.Speed = 1000f;
                tc.NextState = TimeSpan.Zero;
            }
        });

        var sawDeletion = false;
        for (var i = 0; i < 100 && !sawDeletion; i++)
        {
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                if (!trainSys.TrainExists())
                    sawDeletion = true;
            });
        }

        Assert.That(sawDeletion, Is.True,
            "The train should have despawned upon reaching the end marker.");

        var sawRespawn = false;
        for (var i = 0; i < 300 && !sawRespawn; i++)
        {
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                if (trainSys.TrainExists())
                    sawRespawn = true;
            });
        }

        Assert.That(sawRespawn, Is.True,
            "A new train should have respawned at the start marker after the cooldown.");
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.TrainLoop), true)]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.TrainDepartureCooldown), 0.5f)]
    public async Task TrainVisitsEveryStopOnEveryCycle()
    {
        var testMap = await Pair.CreateTestMap();
        var grid = testMap.Grid;
        var gridUid = grid.Owner;

        const float speed = 20f;

        await Server.WaitPost(() =>
        {
            SSpawnAtPosition("TrainStartMarker", new EntityCoordinates(gridUid, new Vector2(0, 0)));
            SSpawnAtPosition("TrainStopMarker", new EntityCoordinates(gridUid, new Vector2(5, 0)));
            SSpawnAtPosition("TrainEndMarker", new EntityCoordinates(gridUid, new Vector2(10, 0)));
        });

        await Server.WaitRunTicks(1);

        var trainSys = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainSystem>();

        await Server.WaitPost(() => trainSys.TrySetupTrain());

        await Server.WaitAssertion(() =>
        {
            var q = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
            Assert.That(q.MoveNext(out _, out var tc), Is.True);
            Assert.That(tc.Route.Count, Is.EqualTo(3),
                "The route should be start -> stop -> end.");

            var length = (tc.Route[^1].Position - tc.Route[0].Position).Length();
            Assert.That(length, Is.GreaterThan(4f),
                "The stored route should span the full start-to-end distance.");
        });

        async Task SpeedUp()
        {
            await Server.WaitPost(() =>
            {
                var q = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
                while (q.MoveNext(out _, out var tc))
                {
                    tc.Speed = speed;
                }
            });
        }

        var cycles = 0;
        var seenStopThisRun = false;
        var trainPresent = false;

        for (var i = 0; i < 4000 && cycles < 2; i++)
        {
            await SpeedUp();
            await Server.WaitRunTicks(1);

            await Server.WaitAssertion(() =>
            {
                var q = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
                var present = q.MoveNext(out _, out var tc);

                if (present)
                {
                    if (tc.RouteIndex >= 1)
                        seenStopThisRun = true;

                    trainPresent = true;
                    return;
                }

                if (trainPresent && seenStopThisRun)
                    cycles++;

                trainPresent = false;
                seenStopThisRun = false;
            });
        }

        Assert.That(cycles, Is.GreaterThanOrEqualTo(2),
            "The train should complete at least two full cycles, visiting the stop and end each time.");
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.TrainLoop), true)]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.TrainDepartureCooldown), 0.25f)]
    public async Task OrderLeftAtStopRegistersAndArrivesOnNextTrain()
    {
        var testMap = await Pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        var stopPos = new Vector2(5, 0);
        var endPos = new Vector2(10, 0);

        var stationSys = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<ServerStationSystem>();
        var money = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainMoneySystem>();

        EntityUid station = default;
        await Server.WaitPost(() =>
        {
            station = Server.EntMan.SpawnEntity("StandardNanotrasenStation", MapCoordinates.Nullspace);
            stationSys.AddGridToStation(station, gridUid);
            money.EnsureMoney(station);
        });

        await Server.WaitPost(() =>
        {
            SSpawnAtPosition("TrainStartMarker", new EntityCoordinates(gridUid, new Vector2(0, 0)));
            SSpawnAtPosition("TrainStopMarker", new EntityCoordinates(gridUid, stopPos));
            SSpawnAtPosition("TrainEndMarker", new EntityCoordinates(gridUid, endPos));
        });

        await Server.WaitRunTicks(1);

        var trainSys = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainSystem>();
        var paper = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<PaperSystem>();

        await Server.WaitPost(() => trainSys.TrySetupTrain());

        var paperUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            paperUid = SSpawnAtPosition("PaperTrainOrder", new EntityCoordinates(gridUid, stopPos));
            paper.SetContent((paperUid, Server.EntMan.GetComponent<PaperComponent>(paperUid)),
                "steel sheet 1x\nsteel sheet 2x\nsteel sheet 3x");
        });

        await Server.WaitAssertion(() => Assert.That(money.GetBalance(station), Is.EqualTo(100)));

        async Task SpeedUp()
        {
            await Server.WaitPost(() =>
            {
                var q = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
                while (q.MoveNext(out _, out var tc))
                    tc.Speed = 40f;
            });
        }

        var registered = false;
        for (var i = 0; i < 2000 && !registered; i++)
        {
            await SpeedUp();
            await Server.WaitRunTicks(1);

            await Server.WaitAssertion(() =>
            {
                var q = Server.EntMan.EntityQueryEnumerator<TrainSpawnerComponent>();
                while (q.MoveNext(out _, out var spawner))
                {
                    if (spawner.PendingDeliveries.Count > 0)
                        registered = true;
                }
            });
        }

        Assert.That(registered, Is.True,
            "Leaving an order form at a stop should register a pending delivery when the train finishes its run.");

        await Server.WaitAssertion(() =>
        {
            Assert.That(money.GetBalance(station), Is.EqualTo(70),
                "The station should have been charged 30 dollars for 6 steel sheets.");

            var total = 0;
            var q = Server.EntMan.EntityQueryEnumerator<TrainSpawnerComponent>();
            while (q.MoveNext(out _, out var spawner))
            {
                Assert.That(spawner.PendingDeliveries, Is.Not.Empty);

                foreach (var delivery in spawner.PendingDeliveries)
                {
                    Assert.That(delivery.Product.Id, Is.EqualTo("SheetSteel1"));
                    total += delivery.Quantity;
                }
            }

            Assert.That(total, Is.EqualTo(6), "All six ordered steel sheets should be queued for delivery.");
        });

        var sawSheetsAboard = false;
        var steelAtDestination = 0;
        for (var i = 0; i < 3000; i++)
        {
            await SpeedUp();
            await Server.WaitRunTicks(1);

            await Server.WaitAssertion(() =>
            {
                var onboardQuery = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
                if (onboardQuery.MoveNext(out var trainUid, out _))
                {
                    var steelQuery = Server.EntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                    while (steelQuery.MoveNext(out _, out var meta, out var xform))
                    {
                        if (meta.EntityPrototype?.ID == "SheetSteel1" && xform.GridUid == trainUid)
                            sawSheetsAboard = true;
                    }
                }

                steelAtDestination = 0;
                var lookup = Server.System<EntityLookupSystem>();
                var nearby = new HashSet<Entity<TransformComponent>>();
                lookup.GetEntitiesInRange(new MapCoordinates(endPos, testMap.MapId), 2f, nearby);

                foreach (var (uid, _) in nearby)
                {
                    var meta = Server.EntMan.GetComponent<MetaDataComponent>(uid);
                    if (meta.EntityPrototype?.ID == "SheetSteel1")
                        steelAtDestination++;
                }
            });
        }

        Assert.That(sawSheetsAboard, Is.True,
            "The ordered sheets should have been loaded onto the next train.");
        Assert.That(steelAtDestination, Is.EqualTo(0),
            "Cargo must not be dumped on the ground at the end marker; it is deleted with the train.");
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.TrainLoop), true)]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.TrainDepartureCooldown), 0.25f)]
    public async Task OrderIsLimitedByCargoPalletSpace()
    {
        var testMap = await Pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        var stopPos = new Vector2(5, 0);

        var stationSys = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<ServerStationSystem>();
        var money = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainMoneySystem>();

        EntityUid station = default;
        await Server.WaitPost(() =>
        {
            station = Server.EntMan.SpawnEntity("StandardNanotrasenStation", MapCoordinates.Nullspace);
            stationSys.AddGridToStation(station, gridUid);
            money.EnsureMoney(station);
        });

        await Server.WaitPost(() =>
        {
            SSpawnAtPosition("TrainStartMarker", new EntityCoordinates(gridUid, new Vector2(0, 0)));
            SSpawnAtPosition("TrainStopMarker", new EntityCoordinates(gridUid, stopPos));
            SSpawnAtPosition("TrainEndMarker", new EntityCoordinates(gridUid, new Vector2(10, 0)));
        });

        await Server.WaitRunTicks(1);

        var trainSys = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<TrainSystem>();
        var paper = Server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<PaperSystem>();

        await Server.WaitPost(() => trainSys.TrySetupTrain());

        var palletCount = 0;
        await Server.WaitAssertion(() =>
        {
            var q = Server.EntMan.EntityQueryEnumerator<CargoPalletComponent, TransformComponent>();
            while (q.MoveNext(out _, out _, out var xform))
            {
                var trainQ = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
                if (!trainQ.MoveNext(out var trainUid, out _))
                    continue;
                if (xform.GridUid == trainUid)
                    palletCount++;
            }
        });

        Assert.That(palletCount, Is.GreaterThan(0));

        var overOrder = palletCount + 5;
        var paperUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            paperUid = SSpawnAtPosition("PaperTrainOrder", new EntityCoordinates(gridUid, stopPos));
            paper.SetContent((paperUid, Server.EntMan.GetComponent<PaperComponent>(paperUid)),
                $"steel sheet {overOrder}x");
        });

        async Task SpeedUp()
        {
            await Server.WaitPost(() =>
            {
                var q = Server.EntMan.EntityQueryEnumerator<TrainComponent>();
                while (q.MoveNext(out _, out var tc))
                    tc.Speed = 40f;
            });
        }

        var registered = false;
        for (var i = 0; i < 2000 && !registered; i++)
        {
            await SpeedUp();
            await Server.WaitRunTicks(1);

            await Server.WaitAssertion(() =>
            {
                var q = Server.EntMan.EntityQueryEnumerator<TrainSpawnerComponent>();
                while (q.MoveNext(out _, out var spawner))
                {
                    if (spawner.PendingDeliveries.Count > 0)
                        registered = true;
                }
            });
        }

        Assert.That(registered, Is.True,
            "The order should still register whatever fits on the train's pallets.");

        await Server.WaitAssertion(() =>
        {
            var total = 0;
            var q = Server.EntMan.EntityQueryEnumerator<TrainSpawnerComponent>();
            while (q.MoveNext(out _, out var spawner))
            {
                foreach (var delivery in spawner.PendingDeliveries)
                    total += delivery.Quantity;
            }

            Assert.That(total, Is.EqualTo(palletCount),
                "Only as many items as there are pallets should be queued.");

            Assert.That(money.GetBalance(station), Is.EqualTo(100 - palletCount * 5),
                "The station should only be charged for the items that fit on the pallets.");
        });
    }
}
