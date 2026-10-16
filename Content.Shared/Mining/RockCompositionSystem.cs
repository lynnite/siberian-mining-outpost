using System.Linq;
using System.Numerics;
using Content.Shared.Destructible;
using Content.Shared.Mining.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.Mining;

public sealed partial class RockCompositionSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private INetManager _net = default!;

    private static readonly ProtoId<RockDistributionPrototype> DefaultDistribution = "SiberianRockDistribution";

    public const float PureChance = 0.02f;

    public const float TraceOreChance = 0.35f;

    public const float SecondaryRockChance = 0.25f;

    public const float MinOreVeinPercent = 60f;

    public const float MaxOreVeinPercent = 95f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RockCompositionComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RockCompositionComponent, DestructionEventArgs>(OnDestroyed);
    }

    private void OnDestroyed(Entity<RockCompositionComponent> ent, ref DestructionEventArgs args)
    {
        if (!_net.IsServer || ent.Comp.DropPrototype is not { } dropProto)
            return;

        var comp = ent.Comp;
        var xform = Transform(ent);
        var coords = xform.Coordinates;
        var amount = _random.Next(comp.MinDrop, comp.MaxDrop + 1);

        ProtoId<OrePrototype>? veinOre = null;
        if (TryComp<OreVeinComponent>(ent, out var vein))
            veinOre = vein.CurrentOre;

        for (var i = 0; i < amount; i++)
        {
            var offset = new Vector2(
                _random.NextFloat(-comp.DropOffset, comp.DropOffset),
                _random.NextFloat(-comp.DropOffset, comp.DropOffset));

            var dropped = Spawn(dropProto, coords.Offset(offset));

            var droppedComp = EnsureComp<RockCompositionComponent>(dropped);
            droppedComp.RockContent = new Dictionary<ProtoId<RockCompositionPrototype>, float>(comp.RockContent);
            droppedComp.OreContent = new Dictionary<ProtoId<OrePrototype>, float>(comp.OreContent);
            droppedComp.DropPrototype = null;
            Dirty(dropped, droppedComp);

            if (veinOre is { } ore)
            {
                var oreComp = EnsureComp<UnidentifiedOreRockComponent>(dropped);
                oreComp.Ore = ore;
                Dirty(dropped, oreComp);
            }
        }
    }

    private void OnMapInit(Entity<RockCompositionComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.RockContent.Count != 0 || ent.Comp.OreContent.Count != 0)
            return;

        ProtoId<OrePrototype>? forcedOre = null;
        if (TryComp<OreVeinComponent>(ent, out var vein) && vein.CurrentOre is { } ore)
            forcedOre = ore;

        RollComposition(ent, null, forcedOre);
    }

    public void RollComposition(
        Entity<RockCompositionComponent> ent,
        RockDistributionPrototype? distribution = null,
        ProtoId<OrePrototype>? forcedOre = null)
    {
        var comp = ent.Comp;
        comp.RockContent.Clear();
        comp.OreContent.Clear();

        distribution ??= _proto.Index(DefaultDistribution);

        if (forcedOre == null && _random.Prob(PureChance))
        {
            var pureRock = PickRock(distribution);
            comp.RockContent[pureRock] = 100f;
            Dirty(ent);
            return;
        }

        var primary = PickRock(distribution);
        var primaryPct = 100f;

        if (_random.Prob(SecondaryRockChance))
        {
            var secondary = PickRock(distribution, primary);
            if (secondary != primary)
            {
                var secondaryPct = _random.NextFloat(5f, 35f);
                comp.RockContent[secondary] = secondaryPct;
                primaryPct = 100f - secondaryPct;
            }
        }

        comp.RockContent[primary] = primaryPct;

        if (forcedOre is { } ore)
        {
            var orePct = _random.NextFloat(MinOreVeinPercent, MaxOreVeinPercent);
            StealPercentageFor(ref comp, orePct);
            comp.OreContent[ore] = orePct;
        }
        else if (_random.Prob(TraceOreChance))
        {
            var trace = PickOre();
            var tracePct = _random.NextFloat(0.1f, 3f);
            StealPercentageFor(ref comp, tracePct);
            comp.OreContent[trace] = tracePct;
        }

        Dirty(ent);
    }

    private static void StealPercentageFor(ref RockCompositionComponent comp, float amount)
    {
        if (comp.RockContent.Count == 0)
            return;

        var total = 0f;
        foreach (var value in comp.RockContent.Values)
            total += value;

        if (total <= 0)
            return;

        var factor = MathF.Max(0f, (total - amount) / total);
        var keys = new List<ProtoId<RockCompositionPrototype>>(comp.RockContent.Keys);
        foreach (var key in keys)
            comp.RockContent[key] *= factor;
    }

    private ProtoId<RockCompositionPrototype> PickRock(RockDistributionPrototype distribution, ProtoId<RockCompositionPrototype>? exclude = null)
    {
        if (distribution.Rocks.Count == 0)
            return "Basalt";

        var total = 0f;
        foreach (var weight in distribution.Rocks.Values)
            total += weight;

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var roll = _random.NextFloat(total);
            var acc = 0f;
            foreach (var (id, weight) in distribution.Rocks)
            {
                acc += weight;
                if (roll > acc)
                    continue;

                if (exclude != null && id == exclude)
                    break;

                return id;
            }
        }

        return distribution.Rocks.Keys.First();
    }

    private ProtoId<OrePrototype> PickOre()
    {
        var ores = new[]
        {
            "OreSteel", "OreCoal", "OreSpaceQuartz", "OreSalt",
            "OreGold", "OreSilver", "OrePlasma", "OreUranium", "OreDiamond",
        };

        return _random.Pick(ores);
    }
}
