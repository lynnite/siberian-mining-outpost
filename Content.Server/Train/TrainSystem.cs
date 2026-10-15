using System.Numerics;
using Content.Server.Cargo.Components;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Events;
using Content.Shared.Item;
using Content.Shared.Station.Systems;
using Content.Shared.Train.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Train;

public sealed partial class TrainSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<ItemComponent> _itemQuery;

    private float Speed => _cfg.GetCVar(CCVars.TrainSpeed);
    private float DepartureCooldown => _cfg.GetCVar(CCVars.TrainDepartureCooldown);
    private bool Loop => _cfg.GetCVar(CCVars.TrainLoop);
    private float SpawnRotation => _cfg.GetCVar(CCVars.TrainSpawnRotation);

    public override void Initialize()
    {
        base.Initialize();

        _xformQuery = GetEntityQuery<TransformComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _itemQuery = GetEntityQuery<ItemComponent>();

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<PostGameMapLoad>(OnPostGameMapLoad);

        SubscribeLocalEvent<TrainComponent, PreventCollideEvent>(OnPreventCollide);

        SubscribeLocalEvent<TrainStartMarkerComponent, MapInitEvent>(OnMarkerMapInit);
        SubscribeLocalEvent<TrainEndMarkerComponent, MapInitEvent>(OnMarkerMapInit);
    }

    private void OnPreventCollide(Entity<TrainComponent> ent, ref PreventCollideEvent args)
    {
        args.Cancelled = true;
    }

    private void OnMarkerMapInit<T>(EntityUid uid, T component, ref MapInitEvent args)
    {
        TrySetupTrain();
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        TrySetupTrain();
    }

    private void OnPostGameMapLoad(PostGameMapLoad ev)
    {
        TrySetupTrain();
    }

    public bool TrySetupTrain()
    {
        if (TrainExists())
            return true;

        if (!TryBuildRoute(out var route, out var startPos, out var toEnd, out var mapId, out var mapUid, out var owner))
            return false;

        var spawner = EnsureComp<TrainSpawnerComponent>(mapUid);
        spawner.Route = route;
        spawner.Owner = owner;
        Dirty(mapUid, spawner);

        return SpawnTrain(route, startPos, toEnd, mapId);
    }

    public bool TrainExists()
    {
        var query = EntityQueryEnumerator<TrainComponent>();
        return query.MoveNext(out _, out _);
    }

    private bool TryBuildRoute(
        out List<TrainRouteNode> route,
        out Vector2 startPos,
        out Vector2 toEnd,
        out MapId mapId,
        out EntityUid mapUid,
        out EntityUid? owner)
    {
        route = new List<TrainRouteNode>();
        startPos = Vector2.Zero;
        toEnd = Vector2.Zero;
        mapId = MapId.Nullspace;
        mapUid = EntityUid.Invalid;
        owner = null;

        if (!TryFindRoute(out var start, out var end, out var stops))
            return false;

        startPos = _transform.GetWorldPosition(start);
        var endPos = _transform.GetWorldPosition(end);
        toEnd = endPos - startPos;
        mapId = Transform(start).MapID;

        owner = _station.GetOwningStation(start);

        if (Transform(start).MapUid is not { } map)
            return false;

        mapUid = map;

        var dir = toEnd.LengthSquared() > 0.001f ? Vector2.Normalize(toEnd) : Vector2.UnitX;
        var origin = startPos;

        stops.Sort((a, b) =>
        {
            var da = Vector2.Dot(_transform.GetWorldPosition(a) - origin, dir);
            var db = Vector2.Dot(_transform.GetWorldPosition(b) - origin, dir);
            return da.CompareTo(db);
        });

        route.Add(new TrainRouteNode(startPos, 0f));

        foreach (var stop in stops)
        {
            var wait = TryComp<TrainStopMarkerComponent>(stop, out var stopComp) ? stopComp.StopDuration : 0f;
            route.Add(new TrainRouteNode(_transform.GetWorldPosition(stop), wait));
        }

        route.Add(new TrainRouteNode(endPos, 0f));
        return true;
    }

    public bool SpawnTrain(List<TrainRouteNode> route, Vector2 startPos, Vector2 toEnd, MapId mapId)
    {
        var path = new ResPath(_cfg.GetCVar(CCVars.TrainGridPath));

        if (!_loader.TryLoadGrid(mapId, path, out var grid))
        {
            Log.Error($"Failed to load train grid from {path}");
            return false;
        }

        var trainUid = grid.Value.Owner;
        var mapUid = Transform(grid.Value.Owner).MapUid ?? grid.Value.Owner;

        _transform.SetCoordinates(trainUid, new EntityCoordinates(mapUid, startPos));

        if (toEnd.LengthSquared() > 0.001f)
        {
            var rotation = toEnd.ToWorldAngle() + Angle.FromDegrees(SpawnRotation);
            _transform.SetWorldRotation(trainUid, rotation);
        }

        var train = EnsureComp<TrainComponent>(trainUid);
        train.Speed = Speed;
        train.Enabled = true;
        train.State = TrainState.Docked;
        train.Direction = 1;
        train.Route = route;
        train.RouteIndex = 0;
        train.TargetPosition = route.Count > 1 ? route[1].Position : startPos;
        train.NextState = _timing.CurTime;
        train.PingPong = !Loop;
        Dirty(trainUid, train);

        _meta.SetEntityName(trainUid, "Delivery Train");

        MakeNonColliding(trainUid);

        var spawnedEv = new TrainSpawnedEvent(trainUid);
        RaiseLocalEvent(ref spawnedEv);

        return true;
    }

    public void MakeNonColliding(EntityUid train)
    {
        var toProcess = new Queue<EntityUid>();
        toProcess.Enqueue(train);

        while (toProcess.Count > 0)
        {
            var current = toProcess.Dequeue();

            if (!_xformQuery.TryGetComponent(current, out var xform))
                continue;

            var children = xform.ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                MakeChildNonColliding(child);
                toProcess.Enqueue(child);
            }
        }
    }

    private void MakeChildNonColliding(EntityUid uid)
    {
        if (!TryComp<FixturesComponent>(uid, out var fixtures))
            return;

        _physicsQuery.TryGetComponent(uid, out var body);

        foreach (var (id, fixture) in fixtures.Fixtures)
        {
            _physics.SetCollisionLayer(uid, id, fixture, 0, fixtures, body: body);
            _physics.SetCollisionMask(uid, id, fixture, 0, fixtures, body: body);
        }
    }

    private void KeepCargoAboard(EntityUid train)
    {
        if (!_gridQuery.HasComponent(train))
            return;

        var pallets = EntityQueryEnumerator<CargoPalletComponent, TransformComponent>();

        while (pallets.MoveNext(out var palletUid, out _, out var palletXform))
        {
            if (palletXform.GridUid != train)
                continue;

            var nearby = new HashSet<Entity<TransformComponent>>();
            _lookup.GetEntitiesInRange(palletXform.Coordinates, 0.5f, nearby, LookupFlags.Uncontained);

            foreach (var (uid, xform) in nearby)
            {
                if (uid == palletUid || uid == train)
                    continue;

                if (!_itemQuery.HasComponent(uid))
                    continue;

                if (xform.ParentUid != xform.GridUid)
                    continue;

                if (xform.GridUid == train && !xform.GridTraversal)
                    continue;

                AttachCargoToTrain(uid, train, xform);
            }
        }
    }

    public void AttachCargoToTrain(EntityUid cargo, EntityUid train, TransformComponent? xform = null)
    {
        if (!_xformQuery.Resolve(cargo, ref xform))
            return;

        if (xform.Anchored)
            return;

        xform.GridTraversal = false;
        _transform.SetParent(cargo, xform, train);

        MakeNonColliding(cargo);
    }

    private bool TryFindRoute(out EntityUid start, out EntityUid end, out List<EntityUid> stops)
    {
        start = EntityUid.Invalid;
        end = EntityUid.Invalid;
        stops = new List<EntityUid>();

        var startQuery = EntityQueryEnumerator<TrainStartMarkerComponent>();
        while (startQuery.MoveNext(out var uid, out _))
        {
            start = uid;
            break;
        }

        var endQuery = EntityQueryEnumerator<TrainEndMarkerComponent>();
        while (endQuery.MoveNext(out var uid, out _))
        {
            end = uid;
            break;
        }

        var stopQuery = EntityQueryEnumerator<TrainStopMarkerComponent>();
        while (stopQuery.MoveNext(out var uid, out _))
        {
            stops.Add(uid);
        }

        return start.IsValid() && end.IsValid();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateSpawners();

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<TrainComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var train, out var xform))
        {
            if (!train.Enabled || xform.MapUid == null)
                continue;

            KeepCargoAboard(uid);

            if (curTime < train.NextState)
                continue;

            switch (train.State)
            {
                case TrainState.Docked:
                    DepartTrain((uid, train, xform));
                    break;
                case TrainState.Travelling:
                    UpdateTravelling((uid, train, xform), frameTime);
                    break;
            }
        }
    }

    private void UpdateSpawners()
    {
        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<TrainSpawnerComponent>();

        while (query.MoveNext(out var mapUid, out var spawner))
        {
            if (spawner.RespawnAt is not { } respawnAt || curTime < respawnAt)
                continue;

            spawner.RespawnAt = null;
            Dirty(mapUid, spawner);

            if (TrainExists())
                continue;

            if (spawner.Route.Count < 2)
                continue;

            var startPos = spawner.Route[0].Position;
            var toEnd = spawner.Route[^1].Position - startPos;

            SpawnTrain(spawner.Route, startPos, toEnd, Transform(mapUid).MapID);
        }
    }

    private void DepartTrain(Entity<TrainComponent, TransformComponent> ent)
    {
        var (uid, train, xform) = ent;

        var departed = new TrainDepartedEvent(uid, xform.Coordinates, train.RouteIndex);
        RaiseLocalEvent(ref departed);

        if (!TryGetNextTarget(train, out var nextPos, out var atEnd))
        {
            train.State = TrainState.Docked;
            train.NextState = _timing.CurTime + TimeSpan.FromSeconds(DepartureCooldown);
            Dirty(uid, train);
            return;
        }

        train.TargetPosition = nextPos;
        train.State = TrainState.Travelling;
        Dirty(uid, train);
    }

    private bool TryGetNextTarget(TrainComponent train, out Vector2 next, out bool atEnd)
    {
        next = default;
        atEnd = false;

        if (train.Route.Count == 0)
            return false;

        var targetIdx = train.RouteIndex;

        if (train.Direction < 0)
        {
            targetIdx -= 1;
        }
        else
        {
            targetIdx += 1;
        }

        if (targetIdx >= train.Route.Count || targetIdx < 0)
            return false;

        next = train.Route[targetIdx].Position;
        atEnd = train.Direction > 0 && targetIdx == train.Route.Count - 1;
        return true;
    }

    private void UpdateTravelling(Entity<TrainComponent, TransformComponent> ent, float frameTime)
    {
        var (uid, train, xform) = ent;

        var currentPos = _transform.GetWorldPosition(xform);
        var targetPos = train.TargetPosition;

        var delta = targetPos - currentPos;
        var distance = delta.Length();

        if (distance <= train.ArriveDistance)
        {
            _transform.SetWorldPosition((uid, xform), targetPos);
            Arrive(ent);
            return;
        }

        var step = train.Speed * frameTime;

        if (step >= distance)
        {
            _transform.SetWorldPosition((uid, xform), targetPos);
            Arrive(ent);
            return;
        }

        var newPos = currentPos + Vector2.Normalize(delta) * step;
        _transform.SetWorldPosition((uid, xform), newPos);
    }

    private void Arrive(Entity<TrainComponent, TransformComponent> ent)
    {
        var (uid, train, xform) = ent;

        var arrivedIdx = train.Direction > 0 ? train.RouteIndex + 1 : train.RouteIndex - 1;

        if (arrivedIdx < 0 || arrivedIdx >= train.Route.Count)
        {
            train.State = TrainState.Docked;
            train.NextState = _timing.CurTime;
            Dirty(uid, train);
            return;
        }

        train.RouteIndex = arrivedIdx;

        var isEnd = train.Direction > 0 && arrivedIdx == train.Route.Count - 1;
        var isStart = train.Direction < 0 && arrivedIdx == 0;

        if (isEnd)
        {
            var completedEv = new TrainRouteCompletedEvent(uid);
            RaiseLocalEvent(ref completedEv);
        }

        var arrivedEv = new TrainArrivedEvent(uid, xform.Coordinates, arrivedIdx, isEnd);
        RaiseLocalEvent(ref arrivedEv);

        if (isEnd)
        {
            if (!train.PingPong)
            {
                DeleteAndQueueRespawn(uid);
                return;
            }

            train.Direction = -1;
            train.State = TrainState.Docked;
            train.NextState = _timing.CurTime + TimeSpan.FromSeconds(DepartureCooldown);
            Dirty(uid, train);
            return;
        }

        if (isStart)
        {
            train.Direction = 1;
        }

        var wait = train.Route[arrivedIdx].Wait;
        train.State = TrainState.Docked;
        train.NextState = _timing.CurTime + TimeSpan.FromSeconds(wait);
        Dirty(uid, train);
    }

    private void DeleteAndQueueRespawn(EntityUid train)
    {
        var mapUid = Transform(train).MapUid;
        QueueDel(train);

        if (mapUid is not { } map || !TryComp<TrainSpawnerComponent>(map, out var spawner))
            return;

        spawner.RespawnAt = _timing.CurTime + TimeSpan.FromSeconds(DepartureCooldown);
        Dirty(map, spawner);
    }
}

[ByRefEvent]
public readonly record struct TrainDepartedEvent(EntityUid Train, EntityCoordinates Coordinates, int RouteIndex);

[ByRefEvent]
public readonly record struct TrainArrivedEvent(EntityUid Train, EntityCoordinates Coordinates, int RouteIndex, bool IsEnd);

[ByRefEvent]
public readonly record struct TrainRouteCompletedEvent(EntityUid Train);

[ByRefEvent]
public readonly record struct TrainSpawnedEvent(EntityUid Train);
