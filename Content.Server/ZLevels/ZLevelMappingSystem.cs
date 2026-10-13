using Content.Shared.ZLevels;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.ZLevels;

public sealed partial class ZLevelMappingSystem : EntitySystem
{
    [Dependency] private ZLevelSystem _zLevels = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    public void SetMapping(EntityUid uid, bool value)
    {
        if (value)
            EnsureComp<ZLevelMappingComponent>(uid);
        else
            RemComp<ZLevelMappingComponent>(uid);
    }

    public bool TryRedirectPlacement(Entity<ZLevelMappingComponent?> mapper, EntityCoordinates attempted, out EntityCoordinates redirected)
    {
        redirected = attempted;

        if (!Resolve(mapper, ref mapper.Comp, false))
            return false;

        var anchorMapUid = Transform(attempted.EntityId).MapUid;
        if (anchorMapUid is not { } anchorMap)
            return false;

        if (mapper.Comp.AnchorLevel is null)
        {
            mapper.Comp.AnchorLevel = anchorMap;
            mapper.Comp.RelativeDepth = mapper.Comp.RelativeDepth == 0 ? 1 : mapper.Comp.RelativeDepth;
        }

        var anchor = mapper.Comp.AnchorLevel!.Value;
        var network = EnsureAnchorNetwork(anchor);

        var editing = GetOrCreateEditingLevel((mapper.Owner, mapper.Comp), network);
        if (editing is not { } editingLevel)
            return false;

        var worldPos = _transform.ToWorldPosition(attempted);
        redirected = new EntityCoordinates(editingLevel, worldPos - _transform.GetWorldPosition(editingLevel));
        return true;
    }

    public bool MoveEditingLevel(Entity<ZLevelMappingComponent?> mapper, int delta)
    {
        if (!Resolve(mapper, ref mapper.Comp, false))
            return false;

        if (mapper.Comp.AnchorLevel is not { } anchor)
            return false;

        var network = EnsureAnchorNetwork(anchor);
        var newRelative = mapper.Comp.RelativeDepth + delta;

        if (newRelative == 0)
        {
            mapper.Comp.EditingLevel = anchor;
            mapper.Comp.RelativeDepth = 0;
            Dirty(mapper);
            return true;
        }

        var anchorDepth = GetDepth(anchor);
        var targetDepth = anchorDepth + newRelative;

        var editing = _zLevels.GetOrCreateLevelAtDepthServer(network, targetDepth);
        if (editing is not { } editingUid)
            return false;

        mapper.Comp.EditingLevel = editingUid;
        mapper.Comp.RelativeDepth = newRelative;
        Dirty(mapper);
        return true;
    }

    public bool ResetEditingLevel(Entity<ZLevelMappingComponent?> mapper)
    {
        if (!Resolve(mapper, ref mapper.Comp, false))
            return false;

        if (mapper.Comp.AnchorLevel is not { } anchor)
            return false;

        mapper.Comp.EditingLevel = anchor;
        mapper.Comp.RelativeDepth = 0;
        Dirty(mapper);
        return true;
    }

    public bool TryPinToEditingLevel(Entity<ZLevelMappingComponent?> mapper, EntityUid placed)
    {
        if (!Resolve(mapper, ref mapper.Comp, false))
            return false;

        if (mapper.Comp.EditingLevel is not { } editing)
            return false;

        _transform.SetParent(placed, editing);
        return true;
    }

    private int GetDepth(EntityUid level)
    {
        return TryComp<ZLevelComponent>(level, out var zMap) ? zMap.Depth : 0;
    }

    private Entity<ZLevelNetworkComponent> EnsureAnchorNetwork(EntityUid anchor)
    {
        if (_zLevels.TryGetMapNetwork(anchor, out var network))
            return network;

        var newNetwork = _zLevels.CreateNetwork();
        _zLevels.TryAddMapToNetwork(newNetwork, anchor, 0);
        return newNetwork;
    }

    private EntityUid? GetOrCreateEditingLevel(Entity<ZLevelMappingComponent> mapper, Entity<ZLevelNetworkComponent> network)
    {
        if (mapper.Comp.AnchorLevel is not { } anchorUid)
            return null;

        var anchorDepth = GetDepth(anchorUid);
        var targetDepth = anchorDepth + (mapper.Comp.RelativeDepth == 0 ? 1 : mapper.Comp.RelativeDepth);

        var level = _zLevels.GetOrCreateLevelAtDepthServer(network, targetDepth);
        if (level is { } levelUid)
        {
            mapper.Comp.EditingLevel = levelUid;
            Dirty(mapper);
        }

        return level;
    }
}
