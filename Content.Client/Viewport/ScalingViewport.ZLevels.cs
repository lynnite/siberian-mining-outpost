using System.Numerics;
using Content.Client.ZLevels;
using Content.Shared.Maps;
using Content.Shared.ZLevels;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ITileDefinitionManager _tile = default!;

    private ZLevelSystem? _zLevels;
    private SharedMapSystem? _mapSystem;

    private EntityQuery<TransformComponent>? _xformQuery;
    private EntityQuery<MapComponent>? _mapQuery;

    private IEye? _fallbackEye;

    public bool TryFindEmptyTiles(EntityUid mapUid)
    {
        if (_xformQuery is null || !_xformQuery.Value.TryComp(mapUid, out var xform))
            return true;

        var drawBox = GetDrawBox();
        var mapId = xform.MapID;

        var corners = new[]
        {
            _eyeManager.ScreenToMap(drawBox.BottomLeft).Position,
            _eyeManager.ScreenToMap(drawBox.BottomRight).Position,
            _eyeManager.ScreenToMap(drawBox.TopLeft).Position,
            _eyeManager.ScreenToMap(drawBox.TopRight).Position,
        };

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (var c in corners)
        {
            if (c.X < minX)
                minX = c.X;
            if (c.Y < minY)
                minY = c.Y;
            if (c.X > maxX)
                maxX = c.X;
            if (c.Y > maxY)
                maxY = c.Y;
        }

        var mapCoordsBottomLeft = new MapCoordinates(new Vector2(minX, minY), mapId);
        var mapCoordsTopRight = new MapCoordinates(new Vector2(maxX, maxY), mapId);

        if (_mapSystem is null || !_mapSystem.TryFindGridAt(mapUid, mapCoordsBottomLeft.Position, out var gridUid, out var grid))
            return true;

        var tileBottomLeft = _mapSystem.TileIndicesFor(gridUid, grid, mapCoordsBottomLeft);
        var tileTopRight = _mapSystem.TileIndicesFor(gridUid, grid, mapCoordsTopRight);

        for (var x = tileBottomLeft.X - 1; x <= tileTopRight.X + 1; x++)
        {
            for (var y = tileBottomLeft.Y - 1; y <= tileTopRight.Y + 1; y++)
            {
                var tile = _mapSystem.GetTileRef(gridUid, grid, new Vector2i(x, y));
                var tileDef = (ContentTileDefinition)_tile[tile.Tile.TypeId];
                if (tileDef.ID == ContentTileDefinition.SpaceID || tile.Tile.IsEmpty)
                    return true;
            }
        }

        return false;
    }

    private void RenderZLevels(IClydeViewport viewport)
    {
        if (_eye is null)
            return;

        _fallbackEye = _eye;

        _xformQuery ??= _entityManager.GetEntityQuery<TransformComponent>();
        _mapQuery ??= _entityManager.GetEntityQuery<MapComponent>();

        _zLevels ??= _entityManager.System<ZLevelSystem>();
        _mapSystem ??= _entityManager.System<SharedMapSystem>();

        var localEntity = _player.LocalEntity;
        if (localEntity is null ||
            !_entityManager.HasComponent<ZLevelViewerComponent>(localEntity.Value) ||
            !_xformQuery.Value.TryComp(localEntity, out var playerXform) ||
            playerXform.MapUid is null)
        {
            viewport.Eye = _fallbackEye;
            viewport.Render();
            return;
        }

        var lookUp = 0;
        if (_entityManager.GetComponent<ZLevelViewerComponent>(localEntity.Value).LookUp)
            lookUp = _zLevels.GetVisibleZLevelsAbove(localEntity.Value, playerXform.MapUid);

        var lowestDepth = 0;
        for (var i = -1; i >= -ZLevelSystem.MaxZLevelsBelowRendering; i--)
        {
            if (_zLevels.TryMapOffset(playerXform.MapUid.Value, i, out _))
                lowestDepth = i;
        }

        for (var depth = lowestDepth; depth <= lookUp; depth++)
        {
            if (depth == 0)
            {
                viewport.Eye = _fallbackEye;
            }
            else
            {
                if (!_zLevels.TryMapOffset(playerXform.MapUid.Value, depth, out var mapUid))
                    continue;

                if (!_mapQuery.Value.TryComp(mapUid, out var mapComp))
                    continue;
                var rotation = new Angle(_fallbackEye.Rotation.Theta + Math.PI);
                var offset = rotation.ToWorldVec() * ZLevelSystem.ZLevelOffset * depth;

                viewport.Eye = new ZEye(lowestDepth, depth, lookUp)
                {
                    Position = new MapCoordinates(_fallbackEye.Position.Position, mapComp.MapId),
                    DrawFov = _fallbackEye.DrawFov && depth >= 0,
                    DrawLight = _fallbackEye.DrawLight,
                    Offset = _fallbackEye.Offset + offset,
                    Rotation = _fallbackEye.Rotation,
                    Scale = _fallbackEye.Scale,
                };
            }

            viewport.ClearColor = depth == lowestDepth ? Color.Black : null;
            viewport.Render();
        }

        Eye = _fallbackEye;
        viewport.Eye = Eye;
    }

    public sealed partial class ZEye(int lowest, int depth, int high) : Robust.Shared.Graphics.Eye
    {
        public readonly int LowestDepth = lowest;
        public readonly int Depth = depth;
        public readonly int HighestDepth = high;
    }
}
