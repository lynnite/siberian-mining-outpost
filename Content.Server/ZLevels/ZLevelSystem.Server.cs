using Content.Shared.Actions;
using Content.Shared.ZLevels;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.ZLevels;

public sealed partial class ZLevelSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private ViewSubscriberSystem _viewSubscriber = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PvsOverrideSystem _pvsOverride = default!;

    private static readonly EntProtoId ZLevelEyeProto = "ZLevelEye";
    private static readonly TimeSpan ViewUpdateRate = TimeSpan.FromSeconds(1f);
    private TimeSpan _nextViewUpdate = TimeSpan.Zero;

    private void InitServerMaps()
    {
        SubscribeLocalEvent<ZLevelComponent, EntityTerminatingEvent>(OnZMapTerminating);
        SubscribeLocalEvent<ZLevelNetworkComponent, ComponentStartup>(OnZNetworkStartup);
        SubscribeLocalEvent<ZLevelNetworkComponent, ComponentShutdown>(OnZNetworkShutdown);
        SubscribeLocalEvent<ZLevelNetworkComponent, EntityTerminatingEvent>(OnZNetworkTerminating);
    }

    private void OnZNetworkStartup(Entity<ZLevelNetworkComponent> ent, ref ComponentStartup args)
    {
        _pvsOverride.AddGlobalOverride(ent);
    }

    private void OnZNetworkShutdown(Entity<ZLevelNetworkComponent> ent, ref ComponentShutdown args)
    {
        _pvsOverride.RemoveGlobalOverride(ent);
    }

    private void OnZNetworkTerminating(Entity<ZLevelNetworkComponent> ent, ref EntityTerminatingEvent args)
    {
        foreach (var (_, mapUid) in ent.Comp.ZLevels)
        {
            if (mapUid is not { } uid || TerminatingOrDeleted(uid))
                continue;

            if (TryComp<ZLevelComponent>(uid, out var zMap) && zMap.NetworkUid == ent.Owner)
                RemComp<ZLevelComponent>(uid);
        }
    }

    private void InitServerView()
    {
        SubscribeLocalEvent<ZLevelViewerComponent, MapInitEvent>(OnViewerInit);
        SubscribeLocalEvent<ZLevelViewerComponent, ComponentRemove>(OnViewerRemoved);
        SubscribeLocalEvent<ZLevelViewerComponent, MapUidChangedEvent>(OnViewerMapUidChanged);
        SubscribeLocalEvent<ZLevelNetworkComponent, ZLevelNetworkUpdatedEvent>(OnNetworkUpdated);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnViewerInit(Entity<ZLevelViewerComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.ActionId);
        _meta.AddFlag(ent, MetaDataFlags.ExtraTransformEvents);
    }
    private void OnViewerRemoved(Entity<ZLevelViewerComponent> ent, ref ComponentRemove args)
    {
        _actions.RemoveAction(ent.Comp.ActionEntity);
        _meta.RemoveFlag(ent, MetaDataFlags.ExtraTransformEvents);

        foreach (var eye in ent.Comp.Eyes)
        {
            QueueDel(eye);
        }

        ent.Comp.Eyes.Clear();
    }

    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        var viewer = EnsureComp<ZLevelViewerComponent>(ev.Entity);
        UpdateViewer((ev.Entity, viewer));
    }

    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        RemComp<ZLevelViewerComponent>(ev.Entity);
    }

    private void OnViewerMapUidChanged(Entity<ZLevelViewerComponent> ent, ref MapUidChangedEvent args)
    {
        UpdateViewer(ent);
    }

    private void OnNetworkUpdated(Entity<ZLevelNetworkComponent> ent, ref ZLevelNetworkUpdatedEvent args)
    {
        var query = EntityQueryEnumerator<ZLevelViewerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var viewer, out var xform))
        {
            if (xform.MapUid is not { } mapUid ||
                !TryComp<ZLevelComponent>(mapUid, out var zMap) ||
                zMap.NetworkUid != ent.Owner)
                continue;

            UpdateViewer((uid, viewer));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (Timing.CurTime < _nextViewUpdate)
            return;

        _nextViewUpdate = Timing.CurTime + ViewUpdateRate;

        var query = EntityQueryEnumerator<ZLevelViewerComponent, TransformComponent>();
        while (query.MoveNext(out _, out var viewer, out var xform))
        {
            var worldPos = TransformSystem.GetWorldPosition(xform);

            foreach (var eye in viewer.Eyes)
            {
                if (TerminatingOrDeleted(eye))
                    continue;

                TransformSystem.SetWorldPosition(eye, worldPos);
            }
        }
    }

    private void UpdateViewer(Entity<ZLevelViewerComponent> ent)
    {
        var eyes = ent.Comp.Eyes;
        foreach (var eye in eyes)
        {
            if (!TerminatingOrDeleted(eye))
                QueueDel(eye);
        }

        eyes.Clear();

        if (!TryComp<ActorComponent>(ent, out var actor))
            return;

        var xform = Transform(ent);
        if (xform.MapUid is not { } map)
            return;

        var globalPos = TransformSystem.GetWorldPosition(xform);

        for (var i = 1; i <= MaxZLevelsBelowRendering; i++)
        {
            if (!TryMapOffset(map, -i, out var mapBelow))
                break;

            eyes.Add(SpawnEye(mapBelow, globalPos, actor.PlayerSession));
        }

        for (var i = 1; i <= MaxZLevelsAboveRendering; i++)
        {
            if (!TryMapOffset(map, i, out var mapAbove))
                break;

            eyes.Add(SpawnEye(mapAbove, globalPos, actor.PlayerSession));
        }
    }

    private EntityUid SpawnEye(EntityUid mapUid, System.Numerics.Vector2 globalPos, ICommonSession session)
    {
        var eye = SpawnAtPosition(ZLevelEyeProto, new EntityCoordinates(mapUid, globalPos));
        Transform(eye).GridTraversal = false;
        _viewSubscriber.AddViewSubscriber(eye, session);
        return eye;
    }
}
