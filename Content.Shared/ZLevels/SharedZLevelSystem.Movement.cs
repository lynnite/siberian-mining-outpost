using System.Numerics;
using Content.Shared.Inventory;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;

namespace Content.Shared.ZLevels;

public abstract partial class SharedZLevelSystem
{
    private readonly List<EntityUid> _dirtyMovementBodies = new();

    public float DistanceToGround(Entity<ZLevelPhysicsComponent?> target)
    {
        if (!Resolve(target, ref target.Comp, false))
            return 0;

        return target.Comp.LocalPosition - target.Comp.CachedGroundHeight;
    }

    private void InitMovement()
    {
        SubscribeLocalEvent<ZLevelComponent, TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<ZLevelPhysicsComponent, MoveEvent>(OnMoveEvent);
        SubscribeLocalEvent<ZLevelPhysicsComponent, ZLevelMapMoveEvent>(OnZLevelMapMove);
    }

    private void OnTileChanged(Entity<ZLevelComponent> ent, ref TileChangedEvent args)
    {
        if (!TryComp<MapGridComponent>(args.Entity, out var grid))
            return;

        foreach (var change in args.Changes)
        {
            var mapCoords = MapSystem.GridTileToWorld(args.Entity, grid, change.GridIndices);
            var half = grid.TileSizeHalfVector;
            var aabb = new Box2(mapCoords.Position - half, mapCoords.Position + half);

            var entities = Lookup.GetEntitiesIntersecting(mapCoords.MapId, aabb, LookupFlags.Uncontained);
            foreach (var uid in entities)
            {
                if (!ZPhysicsQuery.TryComp(uid, out var zComp))
                    continue;

                RequestCacheMovement((uid, zComp), force: true);
                RefreshBody((uid, zComp));
            }
        }
    }

    private void RequestCacheMovement(Entity<ZLevelPhysicsComponent> entity, bool force = false)
    {
        var tile = TransformSystem.GetGridOrMapTilePosition(entity);

        if (tile == entity.Comp.CachedTile && !force)
            return;

        entity.Comp.CachedTile = tile;
        entity.Comp.CachedGroundHeight = ComputeGroundHeightInternal((entity, entity), out var sticky);
        entity.Comp.CachedStickyGround = sticky;
    }

    private void OnMoveEvent(Entity<ZLevelPhysicsComponent> entity, ref MoveEvent args)
    {
        if (_dirtyMovementBodies.Contains(entity))
            return;

        _dirtyMovementBodies.Add(entity);
    }

    private void OnZLevelMapMove(Entity<ZLevelPhysicsComponent> ent, ref ZLevelMapMoveEvent args)
    {
        ent.Comp.CurrentZLevel = args.CurrentZLevel;
        DirtyField(ent, ent.Comp, nameof(ZLevelPhysicsComponent.CurrentZLevel));
        RequestCacheMovement(ent, force: true);
    }

    private float ComputeGroundHeightInternal(Entity<ZLevelPhysicsComponent?> target, out bool stickyGround, int maxFloors = 1)
    {
        stickyGround = false;

        if (!Resolve(target, ref target.Comp, false))
            return 0;

        var xform = Transform(target);
        if (xform.MapUid is not { } mapUid || !ZMapQuery.TryComp(mapUid, out _))
            return 0;

        var worldPos = TransformSystem.GetWorldPosition(target);

        var checkingMap = mapUid;

        for (var floor = 0; floor <= maxFloors; floor++)
        {
            if (floor != 0)
            {
                if (!TryMapOffset(mapUid, -floor, out var below))
                    return -(floor - 1) - 0.1f;

                checkingMap = below;
            }

            if (!MapSystem.TryFindGridAt(checkingMap, worldPos, out var gridUid, out var grid))
                continue;

            var gridTile = MapSystem.WorldToTile(gridUid, grid, worldPos);

            var query = MapSystem.GetAnchoredEntities(gridUid, grid, gridTile);
            while (query.MoveNext(out var uid))
            {
                if (!ZHighGroundQuery.TryComp(uid, out var heightComp))
                    continue;

                var dir = Transform(uid.Value).LocalRotation.GetCardinalDir();

                var gridLocal = MapSystem.WorldToLocal(gridUid, grid, worldPos);
                var local = new Vector2((gridLocal.X % 1 + 1) % 1, (gridLocal.Y % 1 + 1) % 1);

                var t = dir switch
                {
                    Direction.East => heightComp.Corner ? (local.X + 1f - local.Y) / 2f : local.X,
                    Direction.West => heightComp.Corner ? (1f - local.X + local.Y) / 2f : 1f - local.X,
                    Direction.North => heightComp.Corner ? (local.X + local.Y) / 2f : local.Y,
                    Direction.South => heightComp.Corner ? (1f - local.X + 1f - local.Y) / 2f : 1f - local.Y,
                    _ => 0.5f,
                };

                t = float.Clamp(t, 0f, 1f);

                var curve = heightComp.HeightCurve;
                if (curve.Count == 0)
                    continue;

                if (curve.Count == 1)
                    return -floor + curve[0];

                var step = 1f / (curve.Count - 1);
                var index = (int)(t / step);
                var frac = (t - index * step) / step;

                var y0 = curve[Math.Clamp(index, 0, curve.Count - 1)];
                var y1 = curve[Math.Clamp(index + 1, 0, curve.Count - 1)];
                var groundYInterp = MathHelper.Lerp(y0, y1, frac);

                if (target.Comp.Velocity < 0 && target.Comp.Velocity > -2f && heightComp.Stick)
                    stickyGround = true;

                return -floor + groundYInterp;
            }

            if (MapSystem.TryGetTileRef(gridUid, grid, gridTile, out var tileRef) && !tileRef.Tile.IsEmpty)
                return -floor;
        }

        return -maxFloors;
    }

    public bool HasTileAbove(EntityUid ent, EntityUid? currentMapUid = null)
    {
        currentMapUid ??= Transform(ent).MapUid;
        if (currentMapUid is not { } mapUid)
            return false;

        if (!TryMapUp(mapUid, out var mapAboveUid))
            return false;

        var worldPos = TransformSystem.GetWorldPosition(ent);
        if (!MapSystem.TryFindGridAt(mapAboveUid, worldPos, out var gridUid, out var grid))
            return false;

        return MapSystem.TryGetTileRef(gridUid, grid, worldPos, out var tileRef) && !tileRef.Tile.IsEmpty;
    }

    public bool HasTileAbove(Vector2i indices, EntityUid mapUid)
    {
        if (!TryMapUp(mapUid, out var mapAboveUid))
            return false;

        if (!GridQuery.TryComp(mapAboveUid, out var mapAboveGrid))
            return false;

        return MapSystem.TryGetTileRef(mapAboveUid, mapAboveGrid, indices, out var tileRef) && !tileRef.Tile.IsEmpty;
    }

    public void SetZPosition(Entity<ZLevelPhysicsComponent?> ent, float newPosition)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        ent.Comp.LocalPosition = newPosition;
        DirtyField(ent, ent.Comp, nameof(ZLevelPhysicsComponent.LocalPosition));
        WakeBody(ent);
    }

    public void UpdateGravityState(Entity<ZLevelPhysicsComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        var ev = new ZLevelCheckGravityEvent();
        RaiseLocalEvent(ent.Owner, ev);

        SetZGravity(ent, ev.Gravity);
    }

    private void SetZGravity(Entity<ZLevelPhysicsComponent?> ent, float newGravityMultiplier)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        ent.Comp.GravityMultiplier = newGravityMultiplier;
        DirtyField(ent, ent.Comp, nameof(ZLevelPhysicsComponent.GravityMultiplier));
        WakeBody(ent);
    }

    public void SetZVelocity(Entity<ZLevelPhysicsComponent?> ent, float newVelocity)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        ent.Comp.Velocity = newVelocity;
        DirtyField(ent, ent.Comp, nameof(ZLevelPhysicsComponent.Velocity));
        WakeBody(ent);
    }

    public void AddZVelocity(Entity<ZLevelPhysicsComponent?> ent, float newVelocity)
    {
        if (!Resolve(ent.Owner, ref ent.Comp, false))
            return;

        ent.Comp.Velocity += newVelocity;
        DirtyField(ent, ent.Comp, nameof(ZLevelPhysicsComponent.Velocity));
        WakeBody(ent);
    }

    public bool TryMove(EntityUid ent, int offset, EntityUid? map = null)
    {
        map ??= Transform(ent).MapUid;
        if (map is not { } currentMap)
            return false;

        if (!TryMapOffset(currentMap, offset, out var targetMap))
            return false;

        if (!MapQuery.TryComp(targetMap, out var targetMapComp))
            return false;

        var worldPos = TransformSystem.GetWorldPosition(ent);
        var worldRot = TransformSystem.GetWorldRotation(ent);

        var beforeEv = new ZLevelBeforeMapMoveEvent(offset, Comp<ZLevelComponent>(targetMap).Depth);
        RaiseLocalEvent(ent, ref beforeEv);

        TransformSystem.SetMapCoordinates(ent, new MapCoordinates(worldPos, targetMapComp.MapId));
        TransformSystem.SetWorldRotation(ent, worldRot);

        var ev = new ZLevelMapMoveEvent(offset, Comp<ZLevelComponent>(targetMap).Depth);
        RaiseLocalEvent(ent, ref ev);

        return true;
    }

    public bool TryMoveUp(EntityUid ent) => TryMove(ent, 1);

    public bool TryMoveDown(EntityUid ent) => TryMove(ent, -1);

    public bool TryMoveDownOrFall(EntityUid ent)
    {
        if (TryMoveDown(ent))
            return true;

        if (Transform(ent).MapUid is { } map)
        {
            var ev = new ZLevelFallOutOfBoundsEvent(ent);
            RaiseLocalEvent(map, ref ev);
        }

        return false;
    }

    private void UpdateDirtyMovement()
    {
        for (var i = _dirtyMovementBodies.Count - 1; i >= 0; i--)
        {
            var uid = _dirtyMovementBodies[i];

            if (!ZPhysicsQuery.TryComp(uid, out var component))
                continue;

            var entity = (uid, component);
            RequestCacheMovement(entity);
            RefreshBody(entity);
        }

        _dirtyMovementBodies.Clear();
    }
}

public sealed class ZLevelChasmAttempt(EntityUid falled) : CancellableEntityEventArgs, IInventoryRelayEvent
{
    public EntityUid Falled = falled;
    public SlotFlags TargetSlots => SlotFlags.All;
}
