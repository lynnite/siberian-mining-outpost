using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.Shared.ZLevels;

public abstract partial class SharedZLevelSystem
{
    private readonly List<EntityUid> _activeBodies = new();
    private TimeSpan _accumulatedTime = TimeSpan.Zero;

    public IReadOnlyList<EntityUid> ActiveBodies => _activeBodies;

    private void InitActivation()
    {
        SubscribeLocalEvent<ZLevelPhysicsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ZLevelPhysicsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ZLevelPhysicsComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
        SubscribeLocalEvent<ZLevelPhysicsComponent, PhysicsBodyTypeChangedEvent>(OnPhysicsBodyTypeChanged);
        SubscribeLocalEvent<ZLevelPhysicsComponent, EntParentChangedMessage>(OnParentChanged);
    }

    private void OnMapInit(Entity<ZLevelPhysicsComponent> entity, ref MapInitEvent args)
    {
        RefreshBody(entity);

        var mapUid = Transform(entity).MapUid;
        if (!ZMapQuery.TryComp(mapUid, out var zLevel))
            return;

        if (entity.Comp.CurrentZLevel == zLevel.Depth)
            return;

        entity.Comp.CurrentZLevel = zLevel.Depth;
        DirtyField(entity, entity.Comp, nameof(ZLevelPhysicsComponent.CurrentZLevel));
    }

    private void OnShutdown(Entity<ZLevelPhysicsComponent> entity, ref ComponentShutdown args)
    {
        SleepBody((entity, entity));
    }

    private void OnAnchorStateChanged(Entity<ZLevelPhysicsComponent> entity, ref AnchorStateChangedEvent args)
    {
        RefreshBody(entity);
    }

    private void OnPhysicsBodyTypeChanged(Entity<ZLevelPhysicsComponent> entity, ref PhysicsBodyTypeChangedEvent args)
    {
        RefreshBody(entity);
    }

    private void OnParentChanged(Entity<ZLevelPhysicsComponent> entity, ref EntParentChangedMessage args)
    {
        RefreshBody(entity);

        if (ZPhysicsQuery.TryComp(args.OldParent, out var oldParentPhysics))
        {
            SetZPosition((entity, entity), oldParentPhysics.LocalPosition);
            return;
        }

        if (ZPhysicsQuery.HasComp(Transform(entity).ParentUid))
            SetZPosition((entity, entity), 0);
    }

    public void WakeBody(Entity<ZLevelPhysicsComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        if (_activeBodies.Contains(entity))
            return;

        entity.Comp.Sleeping = false;
        entity.Comp.SleepTimer = 0f;
        _activeBodies.Add(entity);

        RequestCacheMovement((entity.Owner, entity.Comp), force: true);
    }

    public void SleepBody(Entity<ZLevelPhysicsComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        entity.Comp.Sleeping = true;
        entity.Comp.SleepTimer = 0f;
        _activeBodies.Remove(entity);
    }

    public void RefreshBody(Entity<ZLevelPhysicsComponent> entity)
    {
        if (TerminatingOrDeleted(entity))
        {
            SleepBody((entity, entity));
            return;
        }

        var transform = Transform(entity);
        var onMap = transform.ParentUid == transform.GridUid || transform.ParentUid == transform.MapUid;

        if (!onMap ||
            transform.Anchored ||
            (_physicsQuery.TryComp(entity, out var physics) && physics.BodyType == BodyType.Static))
        {
            SleepBody((entity, entity));
            return;
        }

        WakeBody((entity, entity));
    }
}
