using Content.Shared.Throwing;

namespace Content.Shared.ZLevels;

public sealed partial class ZLevelThrowingSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnThrown(Entity<ZLevelPhysicsComponent> ent, ref ThrownEvent args)
    {
        ent.Comp.Disabled = true;
        DirtyField(ent, ent.Comp, nameof(ZLevelPhysicsComponent.Disabled));
    }

    [SubscribeLocalEvent]
    private void OnStopThrow(Entity<ZLevelPhysicsComponent> ent, ref StopThrowEvent args)
    {
        ent.Comp.Disabled = false;
        DirtyField(ent, ent.Comp, nameof(ZLevelPhysicsComponent.Disabled));
    }
}
