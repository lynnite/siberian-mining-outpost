using Robust.Shared.GameStates;

namespace Content.Shared.Mining.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XrfScannerComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Range = 2f;
}
