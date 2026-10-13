using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.ZLevels;
using Robust.Shared.Console;

namespace Content.Server.ZLevels;

[AdminCommand(AdminFlags.Mapping)]
public sealed partial class ZLevelCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;

    public string Command => "zlevel";
    public string Description => "Inspect and manipulate Z-level networks.";
    public string Help =>
        "zlevel add <map-uid> <depth>  - register a map into a Z-level network at a depth\n" +
        "zlevel up <entity-uid>       - move an entity one Z-level up\n" +
        "zlevel down <entity-uid>     - move an entity one Z-level down\n" +
        "zlevel map <entity-uid>      - toggle Z-level mapping mode on an entity\n" +
        "zlevel mapup <entity-uid>    - move the mapper's editing level up one\n" +
        "zlevel mapdown <entity-uid>  - move the mapper's editing level down one";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Not enough arguments.\n" + Help);
            return;
        }

        var zSys = _entManager.System<ZLevelSystem>();

        switch (args[0].ToLowerInvariant())
        {
            case "add":
            {
                if (args.Length != 3)
                {
                    shell.WriteError("Usage: zlevel add <map-uid> <depth>");
                    return;
                }

                if (!NetEntity.TryParse(args[1], out var mapNet) || !_entManager.TryGetEntity(mapNet, out var mapUid))
                {
                    shell.WriteError("Invalid map uid.");
                    return;
                }

                if (!int.TryParse(args[2], out var depth))
                {
                    shell.WriteError("Invalid depth.");
                    return;
                }

                var networkEnt = _entManager.Spawn();
                var network = _entManager.EnsureComponent<ZLevelNetworkComponent>(networkEnt);

                if (zSys.TryAddMapToNetwork((networkEnt, network), mapUid.Value, depth))
                    shell.WriteLine($"Registered {mapUid} at depth {depth} in network {networkEnt}.");
                else
                    shell.WriteError("Failed to register map.");

                return;
            }

            case "up":
            case "down":
            {
                if (!TryGetEntity(shell, args[1], out var target))
                    return;

                var offset = args[0].Equals("up", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                if (zSys.TryMove(target, offset))
                    shell.WriteLine($"Moved {target} {(offset > 0 ? "up" : "down")}.");
                else
                    shell.WriteError("No Z-level in that direction.");

                return;
            }

            case "map":
            {
                if (!TryGetEntity(shell, args[1], out var mapper))
                    return;

                var mapping = _entManager.EnsureComponent<ZLevelMappingComponent>(mapper);
                shell.WriteLine($"Z-level mapping mode enabled for {mapper} (editing level: {mapping.EditingLevel?.ToString() ?? "none"}).");
                return;
            }

            case "mapup":
            case "mapdown":
            {
                if (!TryGetEntity(shell, args[1], out var mapperEnt))
                    return;

                if (!_entManager.TryGetComponent<ZLevelMappingComponent>(mapperEnt, out var mappingComp))
                {
                    shell.WriteError("Entity is not in mapping mode.");
                    return;
                }

                var mappingSys = _entManager.System<ZLevelMappingSystem>();
                var delta = args[0].Equals("mapup", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                if (mappingSys.MoveEditingLevel((mapperEnt, mappingComp), delta))
                    shell.WriteLine($"Editing level now at relative depth {mappingComp.RelativeDepth}.");
                else
                    shell.WriteError("Could not move the editing level.");

                return;
            }

            default:
                shell.WriteError("Unknown subcommand.\n" + Help);
                return;
        }
    }

    private bool TryGetEntity(IConsoleShell shell, string arg, out EntityUid entity)
    {
        entity = EntityUid.Invalid;

        if (!NetEntity.TryParse(arg, out var net) || !_entManager.TryGetEntity(net, out var uid))
        {
            shell.WriteError("Invalid entity uid.");
            return false;
        }

        entity = uid.Value;
        return true;
    }
}
