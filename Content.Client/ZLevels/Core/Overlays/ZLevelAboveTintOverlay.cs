using System.Numerics;
using Content.Client.Viewport;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client.ZLevels.Core.Overlays;

public sealed partial class ZLevelAboveTintOverlay : Overlay
{
    [Dependency] private IEntityManager _entity = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public float TintPerLevel = 0.1f;

    public float MaxTint = 0.5f;

    public Color TintColor = Color.Black;

    public ZLevelAboveTintOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not ScalingViewport.ZEye zeye)
            return false;

        return zeye.Depth > 0 && args.MapId != MapId.Nullspace;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not ScalingViewport.ZEye zeye)
            return;

        var alpha = Math.Min(MaxTint, TintPerLevel * zeye.Depth);

        if (zeye.Depth == zeye.HighestDepth)
            alpha = Math.Min(MaxTint + 0.2f, alpha + 0.2f);

        var color = TintColor.WithAlpha(alpha);
        args.WorldHandle.DrawRect(args.WorldBounds, color);
    }
}
