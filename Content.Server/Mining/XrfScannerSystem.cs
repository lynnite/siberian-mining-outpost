using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Mining;
using Content.Shared.Mining.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Mining;

public sealed partial class XrfScannerSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XrfScannerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<XrfScannerComponent, XrfScanDoAfterEvent>(OnDoAfter);
    }

    private void OnAfterInteract(Entity<XrfScannerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target || !args.CanReach)
            return;

        if (!HasComp<RockCompositionComponent>(target))
            return;

        if (!_transform.InRange(Transform(ent).Coordinates, Transform(target).Coordinates, ent.Comp.Range))
        {
            _popup.PopupEntity(Loc.GetString("xrf-scanner-too-far"), ent, args.User);
            return;
        }

        args.Handled = true;

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            args.User,
            1.5f,
            new XrfScanDoAfterEvent(),
            ent.Owner,
            target: target,
            used: ent.Owner)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private void OnDoAfter(Entity<XrfScannerComponent> ent, ref XrfScanDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;

        if (!TryComp<RockCompositionComponent>(target, out var comp))
            return;

        args.Handled = true;

        if (!_ui.HasUi(ent.Owner, XrfScannerUiKey.Key))
            return;

        var state = BuildState(target, comp);
        _ui.SetUiState(ent.Owner, XrfScannerUiKey.Key, state);
        _ui.OpenUi(ent.Owner, XrfScannerUiKey.Key, args.User);
    }

    private XrfScannerBoundUserInterfaceState BuildState(EntityUid target, RockCompositionComponent comp)
    {
        var rocks = new List<XrfEntry>();
        foreach (var (id, pct) in comp.RockContent)
        {
            if (!_proto.TryIndex(id, out RockCompositionPrototype? rock))
                continue;

            var (path, statename) = ResolveSprite(rock.Sprite);
            rocks.Add(new XrfEntry(Loc.GetString(rock.Name), pct, path, statename));
        }

        var ores = new List<XrfEntry>();
        foreach (var (id, pct) in comp.OreContent)
        {
            if (!_proto.TryIndex(id, out OrePrototype? ore))
                continue;

            var name = _proto.TryIndex(ore.OreEntity, out var entProto)
                ? entProto.Name
                : id.Id;

            var (path, statename) = ResolveOreSprite(ore);
            ores.Add(new XrfEntry(name, pct, path, statename));
        }

        return new XrfScannerBoundUserInterfaceState(GetNetEntity(target), rocks, ores);
    }

    private static (string Path, string State) ResolveSprite(SpriteSpecifier sprite)
    {
        return sprite switch
        {
            SpriteSpecifier.Rsi rsi => (rsi.RsiPath.ToString(), rsi.RsiState),
            SpriteSpecifier.Texture tex => (tex.TexturePath.ToString(), string.Empty),
            _ => (string.Empty, string.Empty),
        };
    }

    private static (string Path, string State) ResolveOreSprite(OrePrototype ore)
    {
        if (ore.OreSprite is { } sprite)
            return ResolveSprite(sprite);

        return ("/Textures/Objects/Materials/ore.rsi", string.Empty);
    }
}
