using Robust.Shared.GameStates;

namespace Content.Shared.ZLevels;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ZLevelClimbableComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan ClimbDelay = TimeSpan.FromSeconds(1.5);
}
