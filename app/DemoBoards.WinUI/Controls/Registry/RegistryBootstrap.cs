namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Idempotent registration of all registry tiers — the C# stand-in for the import-time <c>register(...)</c>
/// calls in <c>registry.js</c>. Hosts call <see cref="EnsureRegistered"/> before rendering through
/// <see cref="NodeRenderer"/>. Tiers are added here as each is ported (currently cardview + card).
/// </summary>
public static class RegistryBootstrap
{
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        ComponentRegistry.RegisterEntries(CardViewEntries.All);
        ComponentRegistry.RegisterEntries(CardEntries.All);
    }
}
