using System.Numerics;
using Content.Shared.Actions;
using Content.Shared.Maps;
using Robust.Shared.Map;

namespace Content.Shared.ZLevels;

public abstract partial class SharedZLevelSystem
{
    [Dependency] protected ITileDefinitionManager TileDefMan = default!;

    private void InitView()
    {
        SubscribeLocalEvent<ZLevelViewerComponent, ZLevelToggleLookUpEvent>(OnToggleLookUp);
    }

    private void OnToggleLookUp(Entity<ZLevelViewerComponent> entity, ref ZLevelToggleLookUpEvent args)
    {
        if (args.Handled)
            return;

        if (entity.Comp.ActionEntity != args.Action.Owner)
            return;

        args.Handled = true;

        entity.Comp.LookUp = !entity.Comp.LookUp;
        DirtyField(entity, entity.Comp, nameof(ZLevelViewerComponent.LookUp));
    }

    public int GetVisibleZLevelsAbove(EntityUid ent, EntityUid? currentMapUid = null)
    {
        currentMapUid ??= Transform(ent).MapUid;
        if (currentMapUid is not { } mapUid)
            return 0;

        if (!TryMapUp(mapUid, out var checkingMap))
            return 0;

        var worldPos = TransformSystem.GetWorldPosition(ent);
        var visibleLevels = 0;

        for (var i = 1; i <= MaxZLevelsAboveRendering; i++)
        {
            if (MapSystem.TryFindGridAt(checkingMap, worldPos, out var gridUid, out var grid) &&
                MapSystem.TryGetTileRef(gridUid, grid, worldPos, out var tileRef) &&
                IsOpaqueTile(tileRef.Tile))
            {
                break;
            }

            visibleLevels++;

            if (i == MaxZLevelsAboveRendering || !TryMapUp(checkingMap, out checkingMap))
                break;
        }

        return visibleLevels;
    }

    public bool HasOpaqueAbove(Vector2 worldPos, EntityUid currentMap)
    {
        if (!TryMapUp(currentMap, out var mapAboveUid))
            return false;

        if (!MapSystem.TryFindGridAt(mapAboveUid, worldPos, out var gridUid, out var grid))
            return false;

        if (!MapSystem.TryGetTileRef(gridUid, grid, worldPos, out var tileRef))
            return false;

        return IsOpaqueTile(tileRef.Tile);
    }

    private bool IsOpaqueTile(Tile tile)
    {
        if (tile.IsEmpty)
            return false;

        if (TileDefMan.TryGetDefinition(tile.TypeId, out var def) &&
            def.ID == ContentTileDefinition.SpaceID)
            return false;

        return true;
    }
}

public sealed partial class ZLevelToggleLookUpEvent : InstantActionEvent
{
}

