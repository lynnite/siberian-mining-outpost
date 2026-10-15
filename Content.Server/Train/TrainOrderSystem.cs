using System.Collections.Generic;
using System.Numerics;
using Content.Server.Cargo.Components;
using Content.Server.Popups;
using Content.Shared.Item;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Train;
using Content.Shared.Train.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Train;

public sealed partial class TrainOrderSystem : EntitySystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private TrainMoneySystem _money = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private TrainSystem _train = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TrainArrivedEvent>(OnTrainArrived);
        SubscribeLocalEvent<TrainRouteCompletedEvent>(OnTrainRouteCompleted);
        SubscribeLocalEvent<TrainSpawnedEvent>(OnTrainSpawned);
    }

    private void OnTrainSpawned(ref TrainSpawnedEvent ev)
    {
        if (Transform(ev.Train).MapUid is not { } map || !TryComp<TrainSpawnerComponent>(map, out var spawner))
            return;

        if (spawner.PendingDeliveries.Count == 0)
            return;

        var pallets = GetEmptyPallets(ev.Train);

        foreach (var delivery in spawner.PendingDeliveries)
        {
            LoadOntoTrain(ev.Train, ref pallets, delivery.Product, delivery.Quantity);
        }

        spawner.PendingDeliveries.Clear();
        Dirty(map, spawner);
    }

    private void OnTrainRouteCompleted(ref TrainRouteCompletedEvent ev)
    {
        if (!TryComp<TrainComponent>(ev.Train, out var train))
            return;

        if (Transform(ev.Train).MapUid is not { } map || !TryComp<TrainSpawnerComponent>(map, out var spawner))
            return;

        if (train.Route.Count == 0)
            return;

        var papers = GetOrderPapersAboard(ev.Train);
        var registered = false;

        foreach (var paper in papers)
        {
            if (RegisterOrder(ev.Train, paper, spawner))
                registered = true;
        }

        if (registered)
            Dirty(map, spawner);
    }

    private bool RegisterOrder(
        EntityUid train,
        Entity<TrainOrderPaperComponent> paper,
        TrainSpawnerComponent spawner)
    {
        if (paper.Comp.Fulfilled)
            return false;

        if (!TryComp<PaperComponent>(paper.Owner, out var paperComp))
            return false;

        var blank = Loc.GetString("paper-train-order-blank");
        if (string.Equals(paperComp.Content.Trim(), blank.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var orders = ParseOrders(paperComp.Content);

        if (orders.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("train-order-invalid"), paper.Owner, PopupType.SmallCaution);
            return false;
        }

        var capacity = GetPalletCount(train) - CountPendingItems(spawner);

        if (capacity <= 0)
        {
            _popup.PopupEntity(Loc.GetString("train-order-no-space"), paper.Owner, PopupType.SmallCaution);
            return false;
        }

        var registeredAny = false;
        var leftover = 0;

        foreach (var (product, quantity) in orders)
        {
            if (quantity <= 0)
                continue;

            var toOrder = Math.Min(quantity, capacity);

            if (toOrder <= 0)
            {
                leftover += quantity;
                continue;
            }

            if (!TryChargeForProduct(spawner, paper, product, toOrder))
                continue;

            spawner.PendingDeliveries.Add(new TrainDelivery(product, toOrder));
            capacity -= toOrder;
            registeredAny = true;
            leftover += quantity - toOrder;
        }

        if (leftover > 0)
        {
            _popup.PopupEntity(Loc.GetString("train-order-no-space"), paper.Owner, PopupType.MediumCaution);
        }

        if (!registeredAny)
            return false;

        _popup.PopupEntity(Loc.GetString("train-order-registered"), paper.Owner);

        paper.Comp.Fulfilled = true;
        Dirty(paper.Owner, paper.Comp);
        QueueDel(paper.Owner);

        return true;
    }

    private bool TryChargeForProduct(
        TrainSpawnerComponent spawner,
        Entity<TrainOrderPaperComponent> paper,
        EntProtoId product,
        int quantity)
    {
        if (!ProtoMan.TryIndex(product, out var proto))
            return false;

        if (!ProtoMan.TryIndex<TrainOrderProductPrototype>(FindProductFor(proto.ID), out var orderProto))
            return false;

        if (spawner.Owner is not { } stationUid)
            return true;

        var cost = orderProto.Price * quantity;

        if (!_money.TrySpend(stationUid, cost))
        {
            _popup.PopupEntity(Loc.GetString("train-order-insufficient-funds",
                ("cost", cost),
                ("balance", _money.GetBalance(stationUid))), paper.Owner, PopupType.MediumCaution);
            return false;
        }

        return true;
    }

    private static int CountPendingItems(TrainSpawnerComponent spawner)
    {
        var total = 0;
        foreach (var delivery in spawner.PendingDeliveries)
            total += delivery.Quantity;

        return total;
    }

    private void OnTrainArrived(ref TrainArrivedEvent ev)
    {
        if (!TryComp<TrainComponent>(ev.Train, out _))
            return;

        if (!ev.IsEnd)
            return;

        DeleteAllCargo(ev.Train);
    }

    private void DeleteAllCargo(EntityUid train)
    {
        var carried = new List<EntityUid>();
        var query = EntityQueryEnumerator<TransformComponent>();

        while (query.MoveNext(out var uid, out var xform))
        {
            if (xform.GridUid != train)
                continue;

            if (!HasComp<ItemComponent>(uid))
                continue;

            carried.Add(uid);
        }

        foreach (var uid in carried)
        {
            QueueDel(uid);
        }
    }

    private void LoadOntoTrain(EntityUid train, ref List<EntityUid> freePallets, EntProtoId product, int quantity)
    {
        if (freePallets.Count == 0)
        {
            Log.Warning($"Train {train} has no free cargo pallets; ordered goods were not loaded.");
            return;
        }

        var toLoad = Math.Min(quantity, freePallets.Count);

        for (var i = 0; i < toLoad; i++)
        {
            var pallet = freePallets[^1];
            freePallets.RemoveAt(freePallets.Count - 1);

            var coords = Transform(pallet).Coordinates;
            var spawned = SpawnAtPosition(product, coords);

            _train.AttachCargoToTrain(spawned, train);
        }
    }

    private List<EntityUid> GetEmptyPallets(EntityUid train)
    {
        var pallets = new List<EntityUid>();
        var palletQuery = EntityQueryEnumerator<CargoPalletComponent, TransformComponent>();

        while (palletQuery.MoveNext(out var palletUid, out _, out var palletXform))
        {
            if (palletXform.GridUid != train)
                continue;

            if (IsPalletEmpty(palletUid, train))
                pallets.Add(palletUid);
        }

        return pallets;
    }

    private bool IsPalletEmpty(EntityUid pallet, EntityUid train)
    {
        var coords = Transform(pallet).Coordinates;
        var entities = new HashSet<Entity<TransformComponent>>();

        _lookup.GetEntitiesInRange(coords, 0.5f, entities);

        foreach (var (uid, xform) in entities)
        {
            if (uid == pallet || uid == train)
                continue;

            if (xform.GridUid != train)
                continue;

            if (!HasComp<ItemComponent>(uid))
                continue;

            return false;
        }

        return true;
    }

    private List<Entity<TrainOrderPaperComponent>> GetOrderPapersAboard(EntityUid train)
    {
        var results = new List<Entity<TrainOrderPaperComponent>>();
        var query = EntityQueryEnumerator<TrainOrderPaperComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var paper, out var xform))
        {
            if (xform.GridUid != train)
                continue;

            results.Add((uid, paper));
        }

        return results;
    }

    private string FindProductFor(string entityProto)
    {
        foreach (var proto in ProtoMan.EnumeratePrototypes<TrainOrderProductPrototype>())
        {
            if (proto.Product == entityProto)
                return proto.ID;
        }

        return string.Empty;
    }

    public List<(EntProtoId Product, int Quantity)> ParseOrders(string text)
    {
        var orders = new List<(EntProtoId, int)>();

        if (string.IsNullOrWhiteSpace(text))
            return orders;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.Length == 0)
                continue;

            if (TryParseOrder(line, out var product, out var quantity))
                orders.Add((product, quantity));
        }

        return orders;
    }

    private int GetPalletCount(EntityUid train)
    {
        var count = 0;
        var query = EntityQueryEnumerator<CargoPalletComponent, TransformComponent>();

        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == train)
                count++;
        }

        return count;
    }

    public bool TryParseOrder(string text, out EntProtoId product, out int quantity)
    {
        product = default!;
        quantity = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalised = text.ToLowerInvariant();

        TrainOrderProductPrototype? best = null;
        var bestLen = 0;

        foreach (var proto in ProtoMan.EnumeratePrototypes<TrainOrderProductPrototype>())
        {
            foreach (var keyword in proto.Keywords)
            {
                var kw = keyword.ToLowerInvariant();
                if (!normalised.Contains(kw))
                    continue;

                if (kw.Length > bestLen)
                {
                    bestLen = kw.Length;
                    best = proto;
                }
            }
        }

        if (best == null)
            return false;

        product = best.Product;
        quantity = ParseQuantity(normalised);
        return quantity > 0;
    }

    private static int ParseQuantity(string text)
    {
        var idx = text.IndexOf('x');
        while (idx >= 0)
        {
            var start = idx - 1;
            while (start >= 0 && char.IsDigit(text[start]))
                start--;

            var numStr = text.Substring(start + 1, idx - start - 1);
            if (int.TryParse(numStr, out var amount) && amount > 0)
                return amount;

            idx = text.IndexOf('x', idx + 1);
        }

        return 1;
    }
}
