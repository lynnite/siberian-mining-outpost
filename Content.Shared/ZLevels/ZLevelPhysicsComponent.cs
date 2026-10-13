using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared.ZLevels;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class ZLevelPhysicsComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Velocity;

    [ViewVariables, AutoNetworkedField]
    public float LocalPosition;

    [DataField, AutoNetworkedField]
    public int CurrentZLevel;

    #region Physics

    [DataField, AutoNetworkedField]
    public float Bounciness = 0.3f;

    [DataField, AutoNetworkedField]
    public float GravityMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public bool Fallable = true;

    [DataField]
    public bool AutoStep = true;

    [DataField]
    public bool VelocityGravity = true;

    [DataField]
    public bool VelocityRaiseEvent;

    #endregion

    #region Visuals

    [DataField]
    public int DrawDepthDefault;

    [DataField]
    public Vector2 SpriteOffsetDefault = Vector2.Zero;

    [ViewVariables]
    public bool VisualZApplied;

    #endregion

    #region Cache

    [ViewVariables]
    public Vector2i? CachedTile;

    [ViewVariables]
    public float CachedGroundHeight;

    [ViewVariables]
    public bool CachedStickyGround;

    #endregion

    #region Sleep

    [DataField]
    public float SleepTimer;

    [ViewVariables]
    public bool Sleeping;

    [DataField]
    public float SleepThreshold = 0.3f;

    [DataField]
    public float TimeToSleep = 2f;

    #endregion

    [DataField, AutoNetworkedField]
    public bool Disabled;
}
