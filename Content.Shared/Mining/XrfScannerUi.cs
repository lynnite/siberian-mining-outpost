using Robust.Shared.Serialization;

namespace Content.Shared.Mining;

[Serializable, NetSerializable]
public enum XrfScannerUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class XrfScannerBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly NetEntity Target;

    public readonly List<XrfEntry> RockEntries;

    public readonly List<XrfEntry> OreEntries;

    public XrfScannerBoundUserInterfaceState(
        NetEntity target,
        List<XrfEntry> rockEntries,
        List<XrfEntry> oreEntries)
    {
        Target = target;
        RockEntries = rockEntries;
        OreEntries = oreEntries;
    }
}

[Serializable, NetSerializable]
public struct XrfEntry
{
    public string Name;
    public float Percentage;
    public string SpritePath;
    public string SpriteState;

    public XrfEntry(string name, float percentage, string spritePath, string spriteState)
    {
        Name = name;
        Percentage = percentage;
        SpritePath = spritePath;
        SpriteState = spriteState;
    }
}
