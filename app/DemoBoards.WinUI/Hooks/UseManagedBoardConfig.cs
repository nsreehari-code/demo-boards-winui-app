using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseManagedBoardConfig — Reactor port of useManagedBoardConfig.js
//
//  Loads a board's resolved config (ui / metadata / layout / board record). The web hook
//  is server-mode only and fetches get-board + get-layout in parallel; the embedded app
//  exposes the same data via EmbeddedBoardClient.GetManagedBoardConfigAsync (which already
//  combines get-board and get-layout). The hook loads on mount / boardId change and tracks
//  a loading flag, returning null config on failure.
// =====================================================================================

/// <summary>The centre-pane layout slice (port of <c>normalizeLayout</c>'s <c>{ kind, canvas }</c>).</summary>
public sealed record ManagedBoardLayout(string Kind, JsonNode? Canvas);

/// <summary>The resolved managed-board config (port of the web <c>managedBoardConfig</c> object).</summary>
public sealed record ManagedBoardConfig(
    JsonObject Ui,
    JsonObject Metadata,
    ManagedBoardLayout? Layout,
    JsonObject? Board);

/// <summary>The object returned by <see cref="HookComponent{TProps}.UseManagedBoardConfig"/>.</summary>
public sealed record ManagedBoardConfigResult(ManagedBoardConfig? Config, bool Loading);

public abstract partial class HookComponent<TProps>
{
    private const string DefaultPaneKind = "infinite-canvas";

    /// <summary>Port of <c>useManagedBoardConfig</c>: the resolved board config plus a loading flag.</summary>
    protected ManagedBoardConfigResult UseManagedBoardConfig(string boardId)
    {
        EmbeddedBoardClient client = App.Current.BoardClient;
        var (config, setConfig) = UseState<ManagedBoardConfig?>(null);
        var (loading, setLoading) = UseState(true);

        // Stable-reference preservation matching the frontend's resolveNextManagedBoardConfig:
        // avoid a setConfig call (and the re-render it triggers) when the server returns the same
        // raw JSON strings as the previous load.
        var previousRawRef = UseRef<ManagedBoardConfigState?>(null);

        UseEffect(() =>
        {
            if (string.IsNullOrWhiteSpace(boardId))
            {
                setLoading(false);
                return () => { };
            }

            bool cancelled = false;
            setLoading(true);

            async Task Load()
            {
                try
                {
                    ManagedBoardConfigState? state = await client.GetManagedBoardConfigAsync(boardId.Trim());
                    if (cancelled)
                    {
                        return;
                    }

                    // Only update config state when content actually changed.
                    ManagedBoardConfigState? prev = previousRawRef.Current;
                    bool changed = prev is null
                        || state?.RawUiJson != prev.RawUiJson
                        || state?.RawMetadataJson != prev.RawMetadataJson
                        || state?.RawLayoutJson != prev.RawLayoutJson
                        || state?.RawBoardJson != prev.RawBoardJson;

                    if (changed)
                    {
                        previousRawRef.Current = state;
                        setConfig(BuildManagedBoardConfig(state));
                    }

                    setLoading(false);
                }
                catch
                {
                    if (cancelled)
                    {
                        return;
                    }

                    setConfig(null);
                    setLoading(false);
                }
            }

            _ = Load();
            return () => { cancelled = true; };
        }, boardId ?? string.Empty);

        return new ManagedBoardConfigResult(config, loading);
    }

    private static ManagedBoardConfig? BuildManagedBoardConfig(ManagedBoardConfigState? state)
    {
        if (state is null)
        {
            return null;
        }

        return new ManagedBoardConfig(
            ParseObjectOrEmpty(state.RawUiJson),
            ParseObjectOrEmpty(state.RawMetadataJson),
            NormalizeManagedBoardLayout(state.RawLayoutJson),
            ParseObjectOrNull(state.RawBoardJson));
    }

    private static ManagedBoardLayout? NormalizeManagedBoardLayout(string? rawLayoutJson)
    {
        if (ParseObjectOrNull(rawLayoutJson) is not JsonObject layout)
        {
            return null;
        }

        string kind = layout.TryGetPropertyValue("kind", out JsonNode? kindNode)
            && kindNode is JsonValue kindValue
            && kindValue.TryGetValue(out string? kindString)
            && kindString.Trim().Length > 0
            ? kindString.Trim()
            : DefaultPaneKind;

        JsonNode? canvas = (layout["canvas"] ?? layout).DeepClone();
        return new ManagedBoardLayout(kind, canvas);
    }

    internal static JsonObject ParseObjectOrEmpty(string? rawJson)
    {
        return ParseObjectOrNull(rawJson) ?? new JsonObject();
    }

    internal static JsonObject? ParseObjectOrNull(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(rawJson) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
