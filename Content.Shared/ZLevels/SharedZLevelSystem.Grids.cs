using System;
using Robust.Shared.Map.Components;

namespace Content.Shared.ZLevels;

public abstract partial class SharedZLevelSystem
{
    public int? TryGetGridZDepth(EntityUid gridUid)
    {
        var mapUid = Transform(gridUid).MapUid;
        return mapUid.HasValue && ZMapQuery.TryComp(mapUid.Value, out var zMap) ? zMap.Depth : null;
    }

    public Vector2i GridTileToWorldTile(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var c = MapSystem.GridTileToWorldPos(gridUid, grid, tile);
        return new Vector2i((int)MathF.Floor(c.X), (int)MathF.Floor(c.Y));
    }
}

