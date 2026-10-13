using Robust.Shared.Physics;

namespace Content.Shared.ZLevels;

public abstract partial class SharedZLevelSystem
{
    public const int MaxZLevelsBelowRendering = 3;

    public const int MaxZLevelsAboveRendering = 3;

    public const float ZLevelOffset = 0.7f;

    public const float ZGravityForce = 9.8f;

    public const float ZVelocityLimit = 20.0f;

    public const int MaxStepsPerFrame = 10;

    public const float ImpactVelocityLimit = 3.5f;

    public const float AirborneHeightThreshold = 0.15f;
}

