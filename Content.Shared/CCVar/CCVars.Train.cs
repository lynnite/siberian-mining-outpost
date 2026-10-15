using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<int> TrainStartingMoney =
        CVarDef.Create("train.starting_money", 100, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float> TrainSpeed =
        CVarDef.Create("train.speed", 2.5f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float> TrainDepartureCooldown =
        CVarDef.Create("train.departure_cooldown", 20f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<string> TrainGridPath =
        CVarDef.Create("train.grid_path", "/Maps/train.yml", CVar.SERVERONLY);

    public static readonly CVarDef<bool> TrainLoop =
        CVarDef.Create("train.loop", true, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float> TrainSpawnRotation =
        CVarDef.Create("train.spawn_rotation", 90f, CVar.SERVER | CVar.REPLICATED);
}

