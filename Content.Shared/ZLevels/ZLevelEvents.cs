using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Shared.ZLevels;

[ByRefEvent]
public struct ZLevelBeforeMapMoveEvent(int offset, int level)
{
    public int Offset = offset;
    public int CurrentZLevel = level;
}

[ByRefEvent]
public struct ZLevelMapMoveEvent(int offset, int level)
{
    public int Offset = offset;
    public int CurrentZLevel = level;
}

[ByRefEvent]
public struct ZLevelFallMapEvent;

[ByRefEvent]
public struct ZLevelHitEvent(float impactPower)
{
    public float ImpactPower = impactPower;
}

[ByRefEvent]
public struct ZLevelGetVelocityEvent(Entity<ZLevelPhysicsComponent> target)
{
    public Entity<ZLevelPhysicsComponent> Target = target;
    public float VelocityDelta;
}

public sealed class ZLevelCheckGravityEvent : EntityEventArgs
{
    public float Gravity = 1f;
}

[ByRefEvent]
public readonly record struct ZLevelBodyStatusChangedEvent(BodyStatus NewStatus);

[ByRefEvent]
public struct ZLevelFallOutOfBoundsEvent(EntityUid player)
{
    public EntityUid Player = player;
}
