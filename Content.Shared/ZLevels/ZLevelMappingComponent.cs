using Robust.Shared.GameStates;

namespace Content.Shared.ZLevels;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ZLevelMappingComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? EditingLevel;

    [DataField, AutoNetworkedField]
    public EntityUid? AnchorLevel;

    [DataField, AutoNetworkedField]
    public int RelativeDepth = 1;
}
