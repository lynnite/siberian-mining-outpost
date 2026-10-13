using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Gravity;
using Content.Shared.ZLevels;
using Robust.Shared.Map.Components;

namespace Content.Server.ZLevels;

public sealed partial class ZLevelSystem : SharedZLevelSystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;

    private static readonly GasMixture BreathableAir = CreateBreathableAir();

    private static GasMixture CreateBreathableAir()
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int)Gas.Oxygen] = 21.824779f;
        moles[(int)Gas.Nitrogen] = 82.10312f;
        return new GasMixture(moles, Atmospherics.T20C);
    }

    public override void Initialize()
    {
        base.Initialize();

        InitServerMaps();
        InitServerView();

        SubscribeLocalEvent<MapGridComponent, GridInitializeEvent>(OnGridInitialized);
    }

    private void OnGridInitialized(EntityUid uid, MapGridComponent component, GridInitializeEvent args)
    {
        if (Transform(uid).MapUid is { } mapUid && HasComp<ZLevelComponent>(mapUid))
            EnsureGravity(uid);
    }

    public static readonly Color DefaultAmbientLight = Color.FromHex("#3a3f4a");

    public void EnsureLevelEnvironment(EntityUid mapUid, Color? ambientLight = null)
    {
        if (!HasComp<MapComponent>(mapUid))
            return;

        _atmosphere.SetMapAtmosphere(mapUid, false, BreathableAir.Clone());
        EnsureGravity(mapUid);

        var light = EnsureComp<MapLightComponent>(mapUid);
        if (ambientLight is { } color)
        {
            light.AmbientLightColor = color;
            Dirty(mapUid, light);
        }
        else if (light.AmbientLightColor.Equals(MapLightComponent.DefaultColor))
        {
            light.AmbientLightColor = DefaultAmbientLight;
            Dirty(mapUid, light);
        }
    }

    private void EnsureGravity(EntityUid uid)
    {
        var gravity = EnsureComp<GravityComponent>(uid);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(uid, gravity);
    }

    public Entity<ZLevelNetworkComponent> CreateNetwork()
    {
        var ent = Spawn();
        var comp = EnsureComp<ZLevelNetworkComponent>(ent);
        return (ent, comp);
    }

    public bool TryCreateLevelMap(Entity<ZLevelNetworkComponent> network, int depth, out EntityUid mapUid)
    {
        mapUid = EntityUid.Invalid;

        if (network.Comp.ZLevels.ContainsKey(depth))
            return false;

        mapUid = MapSystem.CreateMap(out _);
        return TryAddMapToNetwork(network, mapUid, depth);
    }

    public bool TryAddMapToNetwork(Entity<ZLevelNetworkComponent> network, EntityUid mapUid, int depth)
    {
        if (!MapQuery.HasComp(mapUid))
        {
            Log.Error($"ZLevel: {mapUid} is not a map entity.");
            return false;
        }

        if (TryGetMapNetwork(mapUid, out _))
        {
            Log.Error($"ZLevel: map {mapUid} is already part of a Z-level network.");
            return false;
        }

        if (network.Comp.ZLevels.ContainsKey(depth))
        {
            Log.Error($"ZLevel: depth {depth} is already occupied in network {network.Owner}.");
            return false;
        }

        network.Comp.ZLevels[depth] = mapUid;
        network.Comp.ZLevelByEntity[mapUid] = depth;

        var zMap = EnsureComp<ZLevelComponent>(mapUid);
        zMap.Depth = depth;
        zMap.NetworkUid = network.Owner;

        if (network.Comp.ZLevels.TryGetValue(depth + 1, out var aboveUid) && aboveUid is { } above)
        {
            zMap.MapAbove = above;
            if (TryComp<ZLevelComponent>(above, out var aboveComp))
            {
                aboveComp.MapBelow = mapUid;
                Dirty(above, aboveComp);
            }
        }

        if (network.Comp.ZLevels.TryGetValue(depth - 1, out var belowUid) && belowUid is { } below)
        {
            zMap.MapBelow = below;
            if (TryComp<ZLevelComponent>(below, out var belowComp))
            {
                belowComp.MapAbove = mapUid;
                Dirty(below, belowComp);
            }
        }

        Dirty(mapUid, zMap);
        RebuildSortedCache(network);
        RaiseLocalEvent(network.Owner, new ZLevelNetworkUpdatedEvent(network.Owner), broadcast: true);

        EnsureLevelEnvironment(mapUid);
        return true;
    }

    public bool TryRemoveMapFromNetwork(EntityUid mapUid)
    {
        if (!TryGetMapNetwork(mapUid, out var network) ||
            !network.Comp.ZLevelByEntity.TryGetValue(mapUid, out var depth))
            return false;

        network.Comp.ZLevels.Remove(depth);
        network.Comp.ZLevelByEntity.Remove(mapUid);

        if (network.Comp.ZLevels.TryGetValue(depth + 1, out var aboveUid) &&
            aboveUid is { } above &&
            TryComp<ZLevelComponent>(above, out var aboveComp))
        {
            aboveComp.MapBelow = null;
            Dirty(above, aboveComp);
        }

        if (network.Comp.ZLevels.TryGetValue(depth - 1, out var belowUid) &&
            belowUid is { } below &&
            TryComp<ZLevelComponent>(below, out var belowComp))
        {
            belowComp.MapAbove = null;
            Dirty(below, belowComp);
        }

        if (!TerminatingOrDeleted(mapUid))
            RemComp<ZLevelComponent>(mapUid);

        RebuildSortedCache(network);
        RaiseLocalEvent(network.Owner, new ZLevelNetworkUpdatedEvent(network.Owner), broadcast: true);
        return true;
    }

    public bool TryGetMapAtDepth(EntityUid networkUid, int depth, out EntityUid mapUid)
    {
        mapUid = EntityUid.Invalid;

        if (!TryComp<ZLevelNetworkComponent>(networkUid, out var network) ||
            !network.ZLevels.TryGetValue(depth, out var uid) ||
            uid is not { } valid)
            return false;

        mapUid = valid;
        return true;
    }

    public EntityUid? GetOrCreateLevelAtDepthServer(Entity<ZLevelNetworkComponent> network, int depth)
    {
        if (network.Comp.ZLevels.TryGetValue(depth, out var existing) && existing is { } existingUid)
            return existingUid;

        var nearestBelow = int.MinValue;
        foreach (var (d, _) in network.Comp.ZLevels)
        {
            if (d < depth && d > nearestBelow)
                nearestBelow = d;
        }

        var start = nearestBelow == int.MinValue ? depth : nearestBelow + 1;

        EntityUid? last = null;
        for (var d = start; d <= depth; d++)
        {
            if (network.Comp.ZLevels.TryGetValue(d, out var found) && found is { } foundUid)
            {
                last = foundUid;
                continue;
            }

            if (!TryCreateLevelMap(network, d, out var created))
                return last;

            last = created;
        }

        return last;
    }

    public void DeleteNetwork(EntityUid networkUid)
    {
        if (!TryComp<ZLevelNetworkComponent>(networkUid, out var network))
            return;

        foreach (var (_, mapUid) in network.ZLevels)
        {
            if (mapUid is { } uid)
                QueueDel(uid);
        }

        QueueDel(networkUid);
    }

    private void OnZMapTerminating(Entity<ZLevelComponent> ent, ref EntityTerminatingEvent args)
    {
        var networkUid = ent.Comp.NetworkUid;

        if (TerminatingOrDeleted(networkUid) || !TryComp<ZLevelNetworkComponent>(networkUid, out var network))
            return;

        network.ZLevels.Remove(ent.Comp.Depth);
        network.ZLevelByEntity.Remove(ent.Owner);
        if (TryComp<ZLevelNetworkComponent>(networkUid, out var networkComp))
            RebuildSortedCache((networkUid, networkComp));
    }

    private void RebuildSortedCache(Entity<ZLevelNetworkComponent> network)
    {
        var comp = network.Comp;
        var list = comp.SortedZLevels;
        list.Clear();

        var min = int.MaxValue;
        var max = int.MinValue;

        foreach (var (depth, mapUid) in comp.ZLevels)
        {
            if (mapUid is not { } uid || !uid.IsValid())
                continue;

            if (depth < min)
                min = depth;
            if (depth > max)
                max = depth;
        }

        if (min > max)
        {
            comp.SortedMin = 0;
            comp.SortedMax = 0;
            Dirty(network);
            return;
        }

        for (var depth = min; depth <= max; depth++)
        {
            list.Add(comp.ZLevels.TryGetValue(depth, out var mapUid) && mapUid is { } uid && uid.IsValid()
                ? uid
                : EntityUid.Invalid);
        }

        comp.SortedMin = min;
        comp.SortedMax = max;
        Dirty(network);
    }
}

public sealed class ZLevelNetworkUpdatedEvent(EntityUid network) : EntityEventArgs
{
    public readonly EntityUid Network = network;
}
