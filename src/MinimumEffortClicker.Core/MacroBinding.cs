namespace MinimumEffortClicker.Core;

/// <summary>
/// What switches a toggle on and off: a keyboard key or mouse button (Key, a virtual key; 0 for none) and/or a
/// controller button. At least one of the two is set. Older saved bindings have only a key.
/// </summary>
public sealed record MacroBinding(Guid MacroId, int Key, bool HoldTrigger, ControllerButton? Controller = null)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool HasKey => Key != 0;
}

public static class MacroBindings
{
    public static void Validate(IReadOnlyList<MacroBinding> bindings, int quickKey, int stopKey)
    {
        if (bindings.Any(b => b is null || b.MacroId == Guid.Empty || !IsValid(b)
            || b.HasKey && (b.Key == quickKey || b.Key == stopKey))
            || bindings.Where(b => b.HasKey).Select(b => b.Key).Distinct().Count() != bindings.Count(b => b.HasKey)
            || bindings.Where(b => b.Controller is not null).Select(b => b.Controller).Distinct().Count() != bindings.Count(b => b.Controller is not null)
            || bindings.Select(b => b.MacroId).Distinct().Count() != bindings.Count)
            throw new ArgumentException("Choose unique key, mouse or controller shortcuts, different from Actions and Stop All.");
    }
    /// <summary>A supported key or mouse button, a supported controller button, or both; not neither.</summary>
    public static bool IsValid(MacroBinding binding) =>
        (binding.HasKey || binding.Controller is not null)
        && (!binding.HasKey || IsSupportedKey(binding.Key))
        && (binding.Controller is not { } button || ControllerButtons.Supported(button));
    public static bool IsSupportedKey(int key) => ShortcutKeys.Supported(key);
    public static string KeyLabel(int key) => ShortcutKeys.Label(key);
}
