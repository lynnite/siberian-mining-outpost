using Robust.Shared.GameStates;

namespace Content.Shared.ZLevels;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class ZLevelViewerComponent : Component
{
    public HashSet<EntityUid> Eyes = new();

    [DataField, AutoNetworkedField]
    public bool LookUp;

    #region Actions

    [DataField]
    public string ActionId = "ZLevelActionToggleLookUp";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    #endregion
}
