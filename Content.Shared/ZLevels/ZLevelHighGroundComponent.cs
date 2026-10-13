using Robust.Shared.GameStates;

namespace Content.Shared.ZLevels;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ZLevelHighGroundComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<float> HeightCurve = new()
    {
        1.05f,
        1.05f,
    };

    [DataField, AutoNetworkedField]
    public bool Stick;

    [DataField, AutoNetworkedField]
    public bool Corner;
}
