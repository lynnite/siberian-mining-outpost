using Content.Shared.CCVar;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.ZLevels;

public abstract partial class SharedZLevelSystem : EntitySystem
{
    [Dependency] protected INetManager NetManager = default!;
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] protected IConfigurationManager Config = default!;

    [Dependency] protected SharedPhysicsSystem PhysicsSystem = default!;
    [Dependency] protected SharedTransformSystem TransformSystem = default!;
    [Dependency] protected EntityLookupSystem Lookup = default!;
    [Dependency] protected SharedMapSystem MapSystem = default!;
    [Dependency] protected SharedPopupSystem PopupSystem = default!;

    [Dependency] protected EntityQuery<ZLevelPhysicsComponent> ZPhysicsQuery = default!;

    [Dependency] protected EntityQuery<MapComponent> MapQuery = default!;
    [Dependency] protected EntityQuery<MapGridComponent> GridQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _transformQuery = default!;

    [Dependency] protected EntityQuery<ZLevelComponent> ZMapQuery = default!;
    [Dependency] protected EntityQuery<ZLevelNetworkComponent> ZNetworkQuery = default!;
    [Dependency] protected EntityQuery<ZLevelHighGroundComponent> ZHighGroundQuery = default!;

    private bool _clientSimulation;
    private TimeSpan _fixedTimestep;

    public override void Initialize()
    {
        base.Initialize();

        Config.OnValueChanged(CCVars.ZLevelsPhysicsClientSimulation, i => _clientSimulation = i, true);
        Config.OnValueChanged(CCVars.ZLevelsPhysicsTickRate, i => _fixedTimestep = TimeSpan.FromSeconds(1d / i), true);

        InitActivation();
        InitMovement();
        InitView();
    }
}


