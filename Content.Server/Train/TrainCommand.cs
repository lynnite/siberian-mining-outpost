using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Train.Components;
using Robust.Shared.Console;
using Robust.Shared.Log;
using Robust.Shared.Timing;

namespace Content.Server.Train;

[AdminCommand(AdminFlags.Mapping)]
public sealed partial class TrainCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private ILogManager _logManager = default!;

    public string Command => "train";
    public string Description => "Inspect and set up the delivery train grid.";
    public string Help =>
        "train setup   - load the train and place it at the start marker (if not already present)\n" +
        "train respawn - delete the existing train and load a fresh one at the start marker\n" +
        "train status  - report whether a train exists and the current route markers";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError("Expected exactly one argument.\n" + Help);
            return;
        }

        var trainSystem = _entManager.System<TrainSystem>();

        switch (args[0].ToLowerInvariant())
        {
            case "setup":
            {
                if (trainSystem.TrySetupTrain())
                    shell.WriteLine("Train set up.");
                else
                    shell.WriteError("Could not set up the train. Ensure a TrainStartMarker and TrainEndMarker exist on a loaded map.");

                return;
            }

            case "respawn":
            {
                var query = _entManager.EntityQueryEnumerator<TrainComponent>();
                while (query.MoveNext(out var trainUid, out _))
                {
                    _entManager.DeleteEntity(trainUid);
                }

                Timer.Spawn(0, () =>
                {
                    if (!trainSystem.TrySetupTrain())
                        _logManager.GetSawmill("train").Error("train respawn failed: no start/end marker on a loaded map");
                });

                shell.WriteLine("Train respawn queued.");
                return;
            }

            case "status":
            {
                shell.WriteLine($"Train exists: {trainSystem.TrainExists()}");

                var startQuery = _entManager.EntityQueryEnumerator<TrainStartMarkerComponent>();
                while (startQuery.MoveNext(out var uid, out _))
                {
                    shell.WriteLine($"  Start marker: {_entManager.ToPrettyString(uid)}");
                }

                var endQuery = _entManager.EntityQueryEnumerator<TrainEndMarkerComponent>();
                while (endQuery.MoveNext(out var uid, out _))
                {
                    shell.WriteLine($"  End marker: {_entManager.ToPrettyString(uid)}");
                }

                var stopQuery = _entManager.EntityQueryEnumerator<TrainStopMarkerComponent>();
                while (stopQuery.MoveNext(out var uid, out _))
                {
                    shell.WriteLine($"  Stop marker: {_entManager.ToPrettyString(uid)}");
                }

                return;
            }

            default:
                shell.WriteError("Unknown subcommand.\n" + Help);
                return;
        }
    }
}

