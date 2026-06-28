using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DemoBoards_WinUI.Config;

/// <summary>
/// File-backed persistence for the user's selected board id — the WinUI analog of the frontend's
/// <c>localStorage['demo-boards.app-config.override']</c>. The override lives next to the embedded
/// runtime state under <c>%LOCALAPPDATA%\DemoBoards.WinUI\board-config.json</c>.
/// </summary>
public static class WinUiBoardIdStore
{
    private const string OverrideFileName = "board-config.json";
    private const string BoardIdProperty = "defaultBoardId";

    private static string OverrideFilePath
    {
        get
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "DemoBoards.WinUI", OverrideFileName);
        }
    }

    /// <summary>Returns the persisted board id override, or <c>null</c> when none has been saved.</summary>
    public static string? LoadOverride()
    {
        try
        {
            string path = OverrideFilePath;
            if (!File.Exists(path))
            {
                return null;
            }

            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
            {
                return null;
            }

            string? boardId = root[BoardIdProperty]?.GetValue<string>()?.Trim();
            return string.IsNullOrWhiteSpace(boardId) ? null : boardId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Persists <paramref name="boardId"/> as the active board override.</summary>
    public static void SaveOverride(string boardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boardId);

        string path = OverrideFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var root = new JsonObject { [BoardIdProperty] = boardId.Trim() };
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
