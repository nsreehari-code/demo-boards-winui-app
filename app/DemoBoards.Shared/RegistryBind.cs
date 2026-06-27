using System.Collections.Generic;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Resolves a bind expression (<c>"namespace.path.to.value"</c>) against the supplied namespaces — a
/// faithful port of <c>registry/lib/bind.js</c>. The engine owns binding so components stay
/// bind-agnostic.
/// </summary>
public static class RegistryBind
{
    /// <summary>Port of <c>resolveBind(namespaces, bind)</c>.</summary>
    public static object? ResolveBind(IReadOnlyDictionary<string, object?>? namespaces, string? bind)
    {
        if (string.IsNullOrEmpty(bind))
        {
            return null;
        }

        IReadOnlyList<string> parts = RegistryPath.PathParts(bind);
        if (parts.Count == 0)
        {
            return null;
        }

        string root = parts[0];
        if (namespaces is null || !namespaces.TryGetValue(root, out object? rootValue))
        {
            return null;
        }

        if (parts.Count == 1)
        {
            return rootValue;
        }

        string rest = string.Join('.', System.Linq.Enumerable.Skip(parts, 1));
        return RegistryPath.DeepGet(rootValue, rest);
    }
}
