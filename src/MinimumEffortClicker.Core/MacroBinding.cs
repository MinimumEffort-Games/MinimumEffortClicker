namespace MinimumEffortClicker.Core;

public sealed record MacroBinding(Guid MacroId, int Key, bool HoldTrigger);

public static class MacroBindings
{
    public static void Validate(IReadOnlyList<MacroBinding> bindings, int quickKey, int stopKey)
    {
        if (bindings.Any(b => b is null || b.MacroId == Guid.Empty || !IsSupportedKey(b.Key)
            || b.Key == quickKey || b.Key == stopKey)
            || bindings.Select(b => b.Key).Distinct().Count() != bindings.Count
            || bindings.Select(b => b.MacroId).Distinct().Count() != bindings.Count)
            throw new ArgumentException("Choose unique key or mouse shortcuts, different from Actions and Stop All.");
    }
    public static bool IsSupportedKey(int key) => ShortcutKeys.Supported(key);
    public static string KeyLabel(int key) => ShortcutKeys.Label(key);
}
