namespace Engine.Tests;
public static class Check
{
    public static void That(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    public static void Throws(Action action, string contains = "")
    {
        try { action(); } catch (Exception e) { That(e.ToString().Contains(contains), e.ToString()); return; }
        throw new Exception("Expected failure: " + contains);
    }
}
