using System.Numerics;
using Content.Client.Viewport;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client.ZLevels.Core.Overlays;

public sealed partial class ZLevelBelowTintOverlay : Overlay
{
    [Dependency] private IEntityManager _entity = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public float TintPerLevel = 0.18f;

    public float MaxTint = 0.7f;

    public Color TintColor = Color.Black;

    public ZLevelBelowTintOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not ScalingViewport.ZEye zeye)
            return false;

        return zeye.Depth < 0 && args.MapId != MapId.Nullspace;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not ScalingViewport.ZEye zeye)
            return;

        var alpha = Math.Min(MaxTint, TintPerLevel * -zeye.Depth);
        var color = TintColor.WithAlpha(alpha);
        args.WorldHandle.DrawRect(args.WorldBounds, color);
    }
}
