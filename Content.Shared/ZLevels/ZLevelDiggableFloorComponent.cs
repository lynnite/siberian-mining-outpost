namespace Content.Shared.ZLevels;

[RegisterComponent]
public sealed partial class ZLevelDiggableFloorComponent : Component
{
    [DataField]
    public bool RemoveTile = true;
}
