using Content.Shared.Mining;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Mining.UI;

[UsedImplicitly]
public sealed class XrfScannerBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private XrfScannerWindow? _window;

    public XrfScannerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<XrfScannerWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is XrfScannerBoundUserInterfaceState cast)
            _window?.Populate(cast);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _window?.Close();
    }
}
