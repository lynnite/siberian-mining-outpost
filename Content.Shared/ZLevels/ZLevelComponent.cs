using Robust.Shared.GameStates;

namespace Content.Shared.ZLevels;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class ZLevelComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public EntityUid NetworkUid;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? MapAbove;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? MapBelow;

    [DataField, AutoNetworkedField]
    public int Depth;
}
