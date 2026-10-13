using Content.Shared.Random;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.ZLevels;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ZLevelMineMarkerComponent : Component
{
    [DataField, AutoNetworkedField]
    public Vector2i Size = new(32, 32);

    [DataField, AutoNetworkedField]
    public int Levels = 3;

    [DataField, AutoNetworkedField]
    public int LevelSpacing = 1;

    [DataField, AutoNetworkedField]
    public EntProtoId WallPrototype = "WallRockSnow";

    [DataField, AutoNetworkedField]
    public string FloorTile = "FloorSnow";

    [DataField, AutoNetworkedField]
    public EntProtoId? DiggableFloorPrototype = "ZLevelMineFloor";

    [DataField, AutoNetworkedField]
    public EntProtoId? BedrockFloorPrototype = "ZLevelMineFloorBedrock";

    [DataField, AutoNetworkedField]
    public float OpenFraction = 0.18f;

    [DataField, AutoNetworkedField]
    public int TunnelCount = 6;

    [DataField, AutoNetworkedField]
    public Vector2i TunnelLength = new(12, 40);

    [DataField, AutoNetworkedField]
    public float TunnelTurnChance = 0.35f;

    [DataField, AutoNetworkedField]
    public float SurfaceOpenFraction = 0.75f;

    [DataField, AutoNetworkedField]
    public ProtoId<WeightedRandomEntityPrototype> OreTable = "ZLevelMineSnowOreWalls";

    [DataField, AutoNetworkedField]
    public float OreChance = 0.06f;

    [DataField, AutoNetworkedField]
    public int VeinsPerLevel = 20;

    [DataField, AutoNetworkedField]
    public Vector2i VeinSize = new(3, 8);

    [DataField, AutoNetworkedField]
    public Color AmbientLight = Color.FromHex("#3a3f4a");

    [DataField, AutoNetworkedField]
    public int Seed;

    [DataField, AutoNetworkedField]
    public int ShaftRadius = 1;
}
