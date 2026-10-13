using System.Numerics;
using Content.Client.Viewport;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;

namespace Content.Client.ZLevels.Core.Overlays;

public sealed class ZLevelDebugOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly Font _font;

    public ZLevelDebugOverlay(IEntityManager entity)
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _font = new VectorFont(cache.GetResource<FontResource>("/EngineFonts/NotoSans/NotoSans-Regular.ttf"), 10);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return args.Viewport.Eye is ScalingViewport.ZEye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not ScalingViewport.ZEye zeye)
            return;

        var offset = new Vector2(12f, 48f + (zeye.Depth - zeye.LowestDepth) * 18f);

        var text = zeye.Depth switch
        {
            0 => "Z-level: 0 (current)",
            > 0 => $"Z-level: +{zeye.Depth}",
            _ => $"Z-level: {zeye.Depth}",
        };

        args.ScreenHandle.DrawString(_font, offset, text, Color.Yellow);
    }
}
