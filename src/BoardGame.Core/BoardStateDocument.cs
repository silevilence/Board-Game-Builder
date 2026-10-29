using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
namespace BoardGame.Runtime;

public static class BoardStateDocument
{
    public static GameState Parse(string text,string location)
    {
        try
        {
            var root=JsonNode.Parse(text) as JsonObject
                ?? throw new InvalidDataException("$: expected object");
            if(root["format"] is not JsonValue format || !format.TryGetValue<int>(out var version) || version!=1)
                throw new InvalidDataException("$.format: expected supported version 1");
            if(root.Any(p=>p.Key is not ("format" or "state" or "resources"))) throw new InvalidDataException("$: unknown state definition field");
            if(root["state"] is not JsonObject) throw new InvalidDataException("$.state: required object");
            if(root.ContainsKey("resources"))
            {
                if(root["resources"] is not JsonObject resources)throw new InvalidDataException("$.resources: expected object");
                foreach(var resource in resources)
                    if(resource.Value is not JsonValue value || !value.TryGetValue<string>(out _))throw new InvalidDataException("$.resources."+resource.Key+": expected path");
                if(resources.Count>0)throw new InvalidDataException("$.resources: migrate definition resources to game.start.json resource plugins");
            }
            try
            {
                var state=root["state"]!.Deserialize<GameState>(Wire.Options) ?? throw new InvalidDataException("required object");
                StateValidation.Validate(state);return state;
            }
            catch(Exception error) when(error is not OutOfMemoryException)
            {throw new InvalidDataException("$.state: "+error.Message,error);}
        }
        catch(Exception error) when(error is not OutOfMemoryException)
        {throw new InvalidDataException(location+": "+error.Message,error);}
    }
}
