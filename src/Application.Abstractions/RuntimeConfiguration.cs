using System.Text.Json;
namespace BoardGame.Kernel;

public static class RuntimeConfiguration
{
    public static JsonElement Empty {get;}=CreateEmpty();
    private static JsonElement CreateEmpty(){using var value=JsonDocument.Parse("{}");return value.RootElement.Clone();}
}
