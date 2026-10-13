using System.Numerics;
using Content.Shared.DoAfter;
using Content.Shared.Movement.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.ZLevels;

public sealed partial class ZLevelClimbSystem : EntitySystem
{
    [Dependency] private SharedZLevelSystem _zLevels = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZLevelClimbableComponent, ZLevelClimbDoAfterEvent>(OnClimbDoAfter);
        SubscribeLocalEvent<ZLevelPhysicsComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<ZLevelClimbableComponent, GetVerbsEvent<AlternativeVerb>>(OnGetClimbVerb);
    }

    private void OnGetClimbVerb(Entity<ZLevelClimbableComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        var user = args.User;

        var userXform = Transform(user);
        if (userXform.MapUid is not { } currentLevel)
            return;

        if (!IsAdjacent(user, ent.Owner))
            return;

        if (!CanClimb(user, ent.Owner, currentLevel, 1))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => TryClimb(user, ent.Owner, 1),
            Text = Loc.GetString("zlevel-climb-up-verb"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/anchor.svg.192dpi.png")),
            Priority = 1,
        });
    }

    public bool IsAdjacent(EntityUid mob, EntityUid obstacle)
    {
        var mobXform = Transform(mob);
        if (mobXform.GridUid is not { } gridUid)
            return false;

        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        var mobTile = _transform.GetGridOrMapTilePosition(mob);
        var obstTile = _transform.GetGridOrMapTilePosition(obstacle);

        return Math.Abs(mobTile.X - obstTile.X) <= 1 && Math.Abs(mobTile.Y - obstTile.Y) <= 1;
    }

    private void OnPreventCollide(Entity<ZLevelPhysicsComponent> ent, ref PreventCollideEvent args)
    {
        if (ent.Comp.Disabled)
            return;

        if (!TryComp<PhysicsComponent>(ent, out var physics) || physics.BodyStatus != BodyStatus.InAir)
            return;

        if (HasComp<ZLevelClimbableComponent>(args.OtherEntity))
            args.Cancelled = true;
    }

    public bool CanClimb(EntityUid mob, EntityUid obstacle, EntityUid currentLevel, int offset)
    {
        if (offset != 1)
            return false;

        if (!HasComp<ZLevelClimbableComponent>(obstacle))
            return false;

        if (!_zLevels.TryMapUp(currentLevel, out var aboveLevel))
            return false;

        if (_zLevels.HasTileAbove(mob, currentLevel))
            return false;

        var obstacleWorldPos = _transform.GetWorldPosition(obstacle);

        if (!_map.TryFindGridAt(aboveLevel, obstacleWorldPos, out var aboveGrid, out var gridComp))
            return false;

        var indices = _map.WorldToTile(aboveGrid, gridComp, obstacleWorldPos);

        if (!_map.TryGetTileRef(aboveGrid, gridComp, indices, out var tileRef) || tileRef.Tile.IsEmpty)
            return false;

        if (IsTileBlocked(aboveGrid, gridComp, indices))
            return false;

        return true;
    }

    private Vector2 GetClimbDestination(EntityUid obstacle, EntityUid currentLevel)
    {
        var obstacleWorldPos = _transform.GetWorldPosition(obstacle);

        if (!_zLevels.TryMapUp(currentLevel, out var aboveLevel))
            return obstacleWorldPos;

        if (!_map.TryFindGridAt(aboveLevel, obstacleWorldPos, out var aboveGrid, out var gridComp))
            return obstacleWorldPos;

        var indices = _map.WorldToTile(aboveGrid, gridComp, obstacleWorldPos);

        return _map.GridTileToWorldPos(aboveGrid, gridComp, indices);
    }

    private bool IsTileBlocked(EntityUid gridUid, MapGridComponent grid, Vector2i indices)
    {
        var query = _map.GetAnchoredEntities(gridUid, grid, indices);
        while (query.MoveNext(out var uid))
        {
            if (HasComp<ZLevelMineFloorComponent>(uid) || HasComp<ZLevelDiggableFloorComponent>(uid))
                continue;

            if (HasComp<ZLevelClimbableComponent>(uid) || HasComp<ZLevelHighGroundComponent>(uid))
                return true;

            if (TryComp<PhysicsComponent>(uid, out var other) && other.BodyType == BodyType.Static)
                return true;

            return true;
        }

        return false;
    }

    public bool TryClimb(EntityUid mob, EntityUid obstacle, int offset)
    {
        var xform = Transform(mob);
        if (xform.MapUid is not { } currentLevel)
            return false;

        if (!CanClimb(mob, obstacle, currentLevel, offset))
            return false;

        var doAfterArgs = new DoAfterArgs(EntityManager, mob, TimeSpan.FromSeconds(1.5), new ZLevelClimbDoAfterEvent(GetNetEntity(mob), offset), obstacle)
        {
            BreakOnMove = true,
        };

        return _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnClimbDoAfter(EntityUid uid, ZLevelClimbableComponent component, ZLevelClimbDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var mob = GetEntity(args.Mob);

        var xform = Transform(mob);
        if (xform.MapUid is not { } currentLevel)
            return;

        if (!CanClimb(mob, uid, currentLevel, args.Offset))
            return;

        var destination = GetClimbDestination(uid, currentLevel);

        if (_zLevels.TryMove(mob, args.Offset))
        {
            _transform.SetWorldPosition(mob, destination);
            _zLevels.SetZPosition(mob, 0.5f);
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("zlevel-climb-up-popup"), mob);
        }
    }
}

[Serializable, NetSerializable]
public sealed partial class ZLevelClimbDoAfterEvent : SimpleDoAfterEvent
{
    public NetEntity Mob;
    public int Offset;

    public ZLevelClimbDoAfterEvent(NetEntity mob, int offset)
    {
        Mob = mob;
        Offset = offset;
    }
}
