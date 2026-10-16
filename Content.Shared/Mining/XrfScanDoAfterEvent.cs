using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Mining;

[Serializable, NetSerializable]
public sealed partial class XrfScanDoAfterEvent : SimpleDoAfterEvent;
