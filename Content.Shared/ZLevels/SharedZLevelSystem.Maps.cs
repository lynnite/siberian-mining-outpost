using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.ZLevels;

public abstract partial class SharedZLevelSystem
{
    public bool TryGetMapNetwork(EntityUid mapUid, out Entity<ZLevelNetworkComponent> zLevel)
    {
        zLevel = default;

        if (!ZMapQuery.TryComp(mapUid, out var zMap))
            return false;

        var networkUid = zMap.NetworkUid;

        if (!networkUid.IsValid() || TerminatingOrDeleted(networkUid))
            return false;

        if (!ZNetworkQuery.TryComp(networkUid, out var network))
        {
            Log.Warning($"ZLevel: network {networkUid} has no {nameof(ZLevelNetworkComponent)}.");
            return false;
        }

        zLevel = (networkUid, network);
        return true;
    }

    public bool TryMapOffset(EntityUid mapUid, int offset, out EntityUid output)
    {
        output = EntityUid.Invalid;

        if (!ZMapQuery.TryComp(mapUid, out var zMap))
            return false;

        if (TryGetMapNetwork(mapUid, out var network) &&
            network.Comp.ZLevels.TryGetValue(zMap.Depth + offset, out var target) &&
            target is { } targetUid)
        {
            output = targetUid;
            return true;
        }

        var neighbour = offset == 1 ? zMap.MapAbove : offset == -1 ? zMap.MapBelow : null;
        if (neighbour is { } uid)
        {
            output = uid;
            return true;
        }

        return false;
    }

    public bool TryMapUp(EntityUid mapUid, out EntityUid above) => TryMapOffset(mapUid, 1, out above);

    public bool TryMapDown(EntityUid mapUid, out EntityUid below) => TryMapOffset(mapUid, -1, out below);

    public bool TryGetZLevelOffset(EntityUid mapA, EntityUid mapB, out int offset)
    {
        offset = 0;

        if (mapA == mapB)
            return true;

        if (!ZMapQuery.TryComp(mapA, out var compA) || !ZMapQuery.TryComp(mapB, out var compB))
            return false;

        if (compA.NetworkUid != compB.NetworkUid)
            return false;

        offset = compB.Depth - compA.Depth;
        return true;
    }

    public Vector2 GetSameLevelPosition(EntityUid fromMap, EntityUid toMap, Vector2 worldPos)
    {
        var fromPos = TransformSystem.GetWorldPosition(fromMap);
        var toPos = TransformSystem.GetWorldPosition(toMap);
        return worldPos + (toPos - fromPos);
    }

    public bool IsOnSameZLevel(EntityUid a, EntityUid b)
    {
        var mapA = Transform(a).MapUid;
        var mapB = Transform(b).MapUid;

        if (mapA == mapB)
            return true;

        if (mapA is not { } aUid || mapB is not { } bUid ||
            !ZMapQuery.HasComp(aUid) || !ZMapQuery.HasComp(bUid))
            return false;

        return TryGetZLevelOffset(aUid, bUid, out var offset) && offset == 0;
    }
}
