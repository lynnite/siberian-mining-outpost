using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<int> ZLevelsPhysicsTickRate =
        CVarDef.Create("zlevels.physics.tick_rate", 60, CVar.ARCHIVE);

    public static readonly CVarDef<bool> ZLevelsPhysicsClientSimulation =
        CVarDef.Create("zlevels.physics.client_simulation", true, CVar.ARCHIVE | CVar.CLIENT);

    public static readonly CVarDef<float> ZLevelsPhysicsGravityForce =
        CVarDef.Create("zlevels.physics.gravity_force", 9.8f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float> ZLevelsPhysicsVelocityLimit =
        CVarDef.Create("zlevels.physics.velocity_limit", 20f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float> ZLevelsPhysicsImpactVelocity =
        CVarDef.Create("zlevels.physics.impact_velocity", 3f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<int> ZLevelsRenderingMaxBelow =
        CVarDef.Create("zlevels.rendering.max_below", 1, CVar.SERVER | CVar.REPLICATED);
}
