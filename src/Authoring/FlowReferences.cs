using System.Text.Json.Nodes;
using BoardGame.Runtime;

namespace BoardGame.Authoring;

internal static class FlowReferences
{
    public static IEnumerable<FlowDiagnostic> Validate(ProjectView view, FlowDefinition graph)
    {
        var formats = new GameAuthorFormats(); using var registration = formats.Register(GameAuthorFormats.StateProvider);
        var format = formats.Resolve(view); if (format == null) yield break;
        var model = format.Read(view);
        var seats = model["seats"]?.AsArray().Select(s => s!["id"]!.GetValue<string>()).ToHashSet() ?? [];
        var entities = model["entities"] is JsonObject objects ? objects.Select(p => p.Key).ToHashSet() :
            model["entities"]?.AsArray().Select(e => e!["id"]!.GetValue<string>()).ToHashSet() ?? [];
        foreach (var node in graph.Nodes)
        {
            foreach (var seat in (node.Winners ?? []).Concat(node.Seat?.Fixed is {} fixedSeat ? [fixedSeat] : []))
                if (!seats.Contains(seat)) yield return new("seat-reference", "Unknown seat ID: " + seat, node.Id);
            foreach (var command in (node.Commands ?? []).Concat((node.Actions ?? []).Where(a => a != null).SelectMany(a => a.Commands ?? [])).Where(c => c != null))
            {
                foreach (var entity in new[] { command.Entity, command.Target }.OfType<string>())
                    if (!entities.Contains(entity)) yield return new("entity-reference", "Unknown entity/container ID: " + entity, node.Id);
                if (command.Seat != null && !seats.Contains(command.Seat)) yield return new("seat-reference", "Unknown seat ID: " + command.Seat, node.Id);
            }
        }
    }
}
