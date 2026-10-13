using Content.Shared.Throwing;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Shared.ZLevels;

public abstract partial class SharedZLevelSystem
{
    public override void Update(float frameTime)
    {
        base.Update(frameTime);


        if (NetManager.IsClient && !_clientSimulation)
            return;

        _accumulatedTime += TimeSpan.FromSeconds(frameTime);

        var steps = 0;
        while (_accumulatedTime >= _fixedTimestep && steps < MaxStepsPerFrame)
        {
            UpdateZPhysics((float)_fixedTimestep.TotalSeconds);
            _accumulatedTime -= _fixedTimestep;
            steps++;
        }
    }

    private void UpdateZPhysics(float frameTime)
    {
        UpdateDirtyMovement();

        for (var i = _activeBodies.Count - 1; i >= 0; i--)
        {
            var uid = _activeBodies[i];

            if (!ZPhysicsQuery.TryComp(uid, out var zPhysics) ||
                !_transformQuery.TryComp(uid, out var xform) ||
                !_physicsQuery.TryComp(uid, out var physics) ||
                !ZMapQuery.HasComp(xform.MapUid))
            {
                _activeBodies.RemoveAt(i);
                continue;
            }

            ProcessZPhysics((uid, zPhysics, physics), frameTime);
        }
    }

    private void ProcessZPhysics(Entity<ZLevelPhysicsComponent, PhysicsComponent> entity, float frameTime)
    {
        var zPhysics = entity.Comp1;

        if (zPhysics.Disabled)
            return;

        var oldVelocity = zPhysics.Velocity;
        var oldHeight = zPhysics.LocalPosition;

        if (zPhysics.VelocityGravity)
            zPhysics.Velocity -= ZGravityForce * zPhysics.GravityMultiplier * frameTime;

        if (zPhysics.VelocityRaiseEvent)
        {
            var velocityEvent = new ZLevelGetVelocityEvent((entity, zPhysics));
            RaiseLocalEvent(entity, ref velocityEvent);
            zPhysics.Velocity += velocityEvent.VelocityDelta * frameTime;
        }

        zPhysics.LocalPosition += zPhysics.Velocity * frameTime;
        var distanceToGround = zPhysics.LocalPosition - zPhysics.CachedGroundHeight;

        if (zPhysics.AutoStep && distanceToGround < 0)
            zPhysics.LocalPosition -= distanceToGround;

        if (zPhysics.CachedStickyGround)
            zPhysics.LocalPosition -= distanceToGround;

        if (zPhysics is { Velocity: < 0, Fallable: true })
        {
            if (distanceToGround <= 0.05f)
            {
                if (float.Abs(zPhysics.Velocity) >= ImpactVelocityLimit)
                {
                    var hitEv = new ZLevelHitEvent(-zPhysics.Velocity);
                    RaiseLocalEvent(entity, ref hitEv);

                    var land = new LandEvent(null, true);
                    RaiseLocalEvent(entity, ref land);
                }

                if (float.Abs(zPhysics.Velocity) < zPhysics.SleepThreshold)
                {
                    zPhysics.Velocity = 0;
                    zPhysics.LocalPosition = zPhysics.CachedGroundHeight;
                }
                else
                {
                    zPhysics.Velocity = -zPhysics.Velocity * zPhysics.Bounciness;
                }
            }
        }

        if (zPhysics.LocalPosition < 0)
        {
            if (TryMoveDownOrFall(entity))
            {
                zPhysics.LocalPosition += 1;
                if (zPhysics is { CachedStickyGround: false, Fallable: true })
                {
                    var fallEv = new ZLevelFallMapEvent();
                    RaiseLocalEvent(entity, ref fallEv);
                }
            }
        }

        if (zPhysics.LocalPosition >= 1)
        {
            if (HasTileAbove(entity))
            {
                if (float.Abs(zPhysics.Velocity) >= ImpactVelocityLimit)
                {
                    var hitEv = new ZLevelHitEvent(zPhysics.Velocity);
                    RaiseLocalEvent(entity, ref hitEv);

                    var land = new LandEvent(null, true);
                    RaiseLocalEvent(entity, ref land);
                }

                zPhysics.LocalPosition = 1;
                zPhysics.Velocity = -zPhysics.Velocity * zPhysics.Bounciness;
            }
            else if (TryMoveUp(entity))
            {
                zPhysics.LocalPosition -= 1;
            }
        }

        if (float.Abs(zPhysics.Velocity) > ZVelocityLimit)
            zPhysics.Velocity = float.Sign(zPhysics.Velocity) * ZVelocityLimit;

        if (float.Abs(oldVelocity - zPhysics.Velocity) > 0.001f)
            DirtyField(entity, zPhysics, nameof(ZLevelPhysicsComponent.Velocity));

        if (float.Abs(oldHeight - zPhysics.LocalPosition) > 0.001f)
            DirtyField(entity, zPhysics, nameof(ZLevelPhysicsComponent.LocalPosition));

        if (zPhysics.VelocityGravity)
        {
            var targetStatus = distanceToGround > AirborneHeightThreshold ? BodyStatus.InAir : BodyStatus.OnGround;
            if (entity.Comp2.BodyStatus != targetStatus)
            {
                PhysicsSystem.SetBodyStatus(entity, entity.Comp2, targetStatus);
                var statusEv = new ZLevelBodyStatusChangedEvent(targetStatus);
                RaiseLocalEvent(entity, ref statusEv);
            }
        }

        SleepUpdate((entity, zPhysics), frameTime);
    }

    private void SleepUpdate(Entity<ZLevelPhysicsComponent> entity, float frameTime)
    {
        var distanceToGround = entity.Comp.LocalPosition - entity.Comp.CachedGroundHeight;
        var almostStopped = float.Abs(entity.Comp.Velocity) < entity.Comp.SleepThreshold &&
                            float.Abs(distanceToGround) <= 0.01f;

        if (!almostStopped)
        {
            entity.Comp.SleepTimer = 0f;
            return;
        }

        entity.Comp.SleepTimer += frameTime;
        if (entity.Comp.SleepTimer < entity.Comp.TimeToSleep)
            return;

        SleepBody((entity, entity));
    }
}
