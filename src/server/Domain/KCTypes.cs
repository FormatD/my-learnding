namespace Learning;
public static class KCTypes
{
    // Design v1.1 defaults; legacy Misconception remains readable/publishable under its own identity.
    public static bool ValidDefinition(string? type)=>type is "Concept" or "Procedure" or "Representation" or "Strategy" or "Application" or "Expression" or "Misconception";
}
