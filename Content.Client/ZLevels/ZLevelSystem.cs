using System.Numerics;
using Content.Client.Viewport;
using Content.Client.ZLevels.Core.Overlays;
using Content.Shared.Camera;
using Content.Shared.ZLevels;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Graphics;
using Robust.Shared.Map.Components;
namespace Content.Client.ZLevels;

public sealed partial class ZLevelSystem : SharedZLevelSystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IOverlayManager _overlay = default!;

    public IEye? CurrentEye => _eyeManager.CurrentEye;

    public EntityUid? LocalViewer => _player.LocalEntity;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZLevelPhysicsComponent, GetEyeOffsetEvent>(OnGetEyeOffset);
        SubscribeLocalEvent<ZLevelPhysicsComponent, ComponentStartup>(OnStartup);

        _overlay.AddOverlay(new ZLevelBelowTintOverlay());
        _overlay.AddOverlay(new ZLevelAboveTintOverlay());
        _overlay.AddOverlay(new ZLevelDebugOverlay(EntityManager));
    }

    private void OnStartup(Entity<ZLevelPhysicsComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (sprite.SnapCardinals)
            return;

        ent.Comp.DrawDepthDefault = sprite.DrawDepth;
        ent.Comp.SpriteOffsetDefault = sprite.Offset;
    }

    internal static float GetVisualLocalPosition(EntityUid uid, ZLevelPhysicsComponent zPhys, TransformComponent xform, EntityQuery<ZLevelPhysicsComponent> zPhysicsQuery)
    {
        if (xform.ParentUid != xform.MapUid && zPhysicsQuery.TryComp(xform.ParentUid, out var parentZPhys))
            return parentZPhys.LocalPosition;

        return zPhys.LocalPosition;
    }

    public float GetScreenOffset(float zDepth)
    {
        var eye = _eyeManager.CurrentEye;
        var zoom = eye.Zoom;

        if (zoom.Y == 0)
            return 0;

        return zoom.Y * ZLevelOffset * -zDepth;
    }

    public int GetLocalZDepth()
    {
        if (_player.LocalEntity is not { } local)
            return 0;

        var mapUid = Transform(local).MapUid;
        return mapUid is { } map && ZMapQuery.TryComp(map, out var zMap) ? zMap.Depth : 0;
    }

    private void OnGetEyeOffset(Entity<ZLevelPhysicsComponent> ent, ref GetEyeOffsetEvent args)
    {
        if (_player.LocalEntity != ent.Owner)
            return;

        if (ent.Comp.Disabled)
            return;

        var rotation = new Angle(_eyeManager.CurrentEye.Rotation * -1);
        var localPosition = GetVisualLocalPosition(ent, ent.Comp, Transform(ent), ZPhysicsQuery);
        args.Offset += rotation.RotateVec(new Vector2(0f, localPosition * ZLevelOffset));
    }
}

internal sealed partial class ZLevelPreAnimSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<ZLevelPhysicsComponent> _zPhysQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;

    internal readonly HashSet<EntityUid> ZVisualEntities = new();

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(AnimationPlayerSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        ZVisualEntities.Clear();

        var candidates = EntityQueryEnumerator<ZLevelPhysicsComponent>();
        while (candidates.MoveNext(out var uid, out var zPhys))
        {
            if (zPhys.LocalPosition == 0f && !zPhys.VisualZApplied)
                continue;

            ZVisualEntities.Add(uid);

            if (zPhys.LocalPosition == 0f || !_xformQuery.TryComp(uid, out var bodyXform) || bodyXform.ChildCount == 0)
                continue;

            var children = bodyXform.ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (_zPhysQuery.HasComp(child))
                    ZVisualEntities.Add(child);
            }
        }

        foreach (var uid in ZVisualEntities)
        {
            if (!_zPhysQuery.TryComp(uid, out var zPhys) ||
                !_spriteQuery.TryComp(uid, out var sprite) ||
                !_xformQuery.TryComp(uid, out var xform))
                continue;

            var localPosition = ZLevelSystem.GetVisualLocalPosition(uid, zPhys, xform, _zPhysQuery);
            _sprite.SetOffset((uid, sprite), zPhys.SpriteOffsetDefault);
            _sprite.SetDrawDepth((uid, sprite), localPosition > 0 ? (int)Shared.DrawDepth.DrawDepth.OverMobs : zPhys.DrawDepthDefault);
            zPhys.VisualZApplied = localPosition != 0f;
        }
    }
}

internal sealed partial class ZLevelPostAnimSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private ZLevelPreAnimSystem _preAnim = default!;
    [Dependency] private EntityQuery<ZLevelPhysicsComponent> _zPhysQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(AnimationPlayerSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        foreach (var uid in _preAnim.ZVisualEntities)
        {
            if (!_zPhysQuery.TryComp(uid, out var zPhys) ||
                !_spriteQuery.TryComp(uid, out var sprite) ||
                !_xformQuery.TryComp(uid, out var xform))
                continue;

            var localPosition = ZLevelSystem.GetVisualLocalPosition(uid, zPhys, xform, _zPhysQuery);

            if (localPosition == 0f)
                continue;

            var rawZ = new Vector2(0, localPosition * SharedZLevelSystem.ZLevelOffset);
            Vector2 zOffset;
            if (sprite.NoRotation)
                zOffset = rawZ;
            else
                zOffset = new Angle(-_xform.GetWorldRotation(xform)).RotateVec(rawZ);

            _sprite.SetOffset((uid, sprite), sprite.Offset + zOffset);
        }
    }
}

