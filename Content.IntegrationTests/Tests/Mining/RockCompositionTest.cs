#nullable enable
using System.Linq;
using Content.Shared.Destructible;
using Content.Shared.Mining;
using Content.Shared.Mining.Components;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Mining;

[TestFixture]
public sealed class RockCompositionTest : GameTest
{
    private static readonly ProtoId<TagPrototype> UnidentifiedRockTag = "UnidentifiedRock";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestRockCompositionWall
  components:
  - type: RockComposition
  - type: Tag
    tags:
    - UnidentifiedRock
";

    [Test]
    public async Task PlainRockGetsComposition()
    {
        EntityUid rock = default;

        await Server.WaitPost(() =>
        {
            var map = SEntMan.System<SharedMapSystem>().CreateMap(out var mapId);
            var grid = SEntMan.System<SharedMapSystem>().CreateGridEntity(mapId).Owner;
            rock = SEntMan.SpawnEntity("WallRockSnow", new EntityCoordinates(grid, default));
        });

        await Server.WaitRunTicks(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<RockCompositionComponent>(rock, out var comp), Is.True,
                "Plain wall rock should have a composition.");
            Assert.That(comp!.RockContent.Count + comp.OreContent.Count, Is.GreaterThan(0),
                "Rock composition should be populated at spawn.");
        });
    }

    [Test]
    public async Task OreVeinIsOreDominant()
    {
        EntityUid rock = default;

        await Server.WaitPost(() =>
        {
            var map = SEntMan.System<SharedMapSystem>().CreateMap(out var mapId);
            var grid = SEntMan.System<SharedMapSystem>().CreateGridEntity(mapId).Owner;
            rock = SEntMan.SpawnEntity("WallRockSnowCoal", new EntityCoordinates(grid, default));
        });

        await Server.WaitRunTicks(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<RockCompositionComponent>(rock, out var comp), Is.True);

            Assert.That(comp!.OreContent.TryGetValue("OreCoal", out var coal), Is.True,
                "Coal vein should contain coal.");
            Assert.That(coal, Is.GreaterThanOrEqualTo(60f),
                "Ore veins must be at least 60% of their ore.");
        });
    }

    [Test]
    public async Task DestroyingRockDropsUnidentifiedSample()
    {
        var before = 0;
        var after = 0;

        await Server.WaitPost(() =>
        {
            var map = SEntMan.System<SharedMapSystem>().CreateMap(out var mapId);
            var grid = SEntMan.System<SharedMapSystem>().CreateGridEntity(mapId).Owner;

            before = CountUnidentified();

            var rock = SEntMan.SpawnEntity("WallRockSnow", new EntityCoordinates(grid, default));

            var destructible = SEntMan.System<SharedDestructibleSystem>();
            destructible.DestroyEntity(rock);
        });

        await Server.WaitRunTicks(2);

        await Server.WaitAssertion(() =>
        {
            after = CountUnidentified();
            Assert.That(after, Is.GreaterThan(before),
                "Destroying a wall rock should drop unidentified rock samples.");
        });
    }

    [Test]
    public async Task OreVeinDropsOreSample()
    {
        await Server.WaitPost(() =>
        {
            var map = SEntMan.System<SharedMapSystem>().CreateMap(out var mapId);
            var grid = SEntMan.System<SharedMapSystem>().CreateGridEntity(mapId).Owner;

            var rock = SEntMan.SpawnEntity("WallRockSnowCoal", new EntityCoordinates(grid, default));
            SEntMan.System<SharedDestructibleSystem>().DestroyEntity(rock);
        });

        await Server.WaitRunTicks(2);

        await Server.WaitAssertion(() =>
        {
            var found = false;
            var query = SEntMan.EntityQueryEnumerator<UnidentifiedOreRockComponent, RockCompositionComponent>();
            while (query.MoveNext(out _, out var oreComp, out var rockComp))
            {
                if (oreComp.Ore != "OreCoal")
                    continue;

                found = true;
                Assert.That(rockComp.OreContent.TryGetValue("OreCoal", out var coal), Is.True,
                    "Dropped coal sample should carry its coal composition.");
                Assert.That(coal, Is.GreaterThanOrEqualTo(60f),
                    "Dropped coal sample should be at least 60% coal.");
            }

            Assert.That(found, Is.True,
                "Destroying a coal vein should drop a sample flagged as an ore rock.");
        });
    }

    private int CountUnidentified()
    {
        var tag = SEntMan.System<TagSystem>();
        var query = SEntMan.EntityQueryEnumerator<TagComponent>();
        var count = 0;
        while (query.MoveNext(out var uid, out _))
        {
            if (tag.HasTag(uid, UnidentifiedRockTag))
                count++;
        }

        return count;
    }
}
