using System;
using System.Net.Http;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.Config;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.State;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls.Registry.BoardConfig;

public sealed record ServerSwitcherProps(string InitialServerUrl);

public sealed class ServerSwitcher : HookComponent<ServerSwitcherProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        EmbeddedBoardClient boardClient = UseEmbeddedClient();
        var (serverUrl, setServerUrl) = UseGlobalState<string>(
            GlobalStateKeys.ServerUrl,
            Props.InitialServerUrl,
            WinUiServerUrlStore.SaveOverride);

        var (healthError, setHealthError) = UseState(string.Empty);
        var (isChecking, setIsChecking) = UseState(false);
        var (isReachable, setIsReachable) = UseState(false);
        var (normalizedServerUrl, setNormalizedServerUrl) = UseState(string.Empty);

        string liveRuntimeServerUrl = NormalizeForDisplay(boardClient.LiveBoardStateServerBaseUri.AbsoluteUri);

        UseEffect(() =>
        {
            bool cancelled = false;
            string candidate = (serverUrl ?? string.Empty).Trim();

            if (candidate.Length == 0)
            {
                setIsChecking(false);
                setIsReachable(false);
                setHealthError("Server URL is required.");
                setNormalizedServerUrl(string.Empty);
                return () => cancelled = true;
            }

            if (!TryNormalizeServerUrl(candidate, out Uri? serverBaseUri, out string validationError) || serverBaseUri is null)
            {
                setIsChecking(false);
                setIsReachable(false);
                setHealthError(validationError);
                setNormalizedServerUrl(string.Empty);
                return () => cancelled = true;
            }

            string nextNormalizedServerUrl = NormalizeForDisplay(serverBaseUri.AbsoluteUri);
            setNormalizedServerUrl(nextNormalizedServerUrl);
            setIsChecking(true);
            setIsReachable(false);
            setHealthError(string.Empty);

            async void ProbeHealthz()
            {
                try
                {
                    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                    using HttpResponseMessage response = await httpClient.GetAsync(new Uri(serverBaseUri, "healthz")).ConfigureAwait(false);
                    if (cancelled)
                    {
                        return;
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        setIsReachable(true);
                        setHealthError(string.Empty);
                    }
                    else
                    {
                        setIsReachable(false);
                        setHealthError($"Server health check failed: {(int)response.StatusCode} {response.ReasonPhrase}".Trim());
                    }
                }
                catch (Exception ex)
                {
                    if (cancelled)
                    {
                        return;
                    }

                    setIsReachable(false);
                    setHealthError($"Server health check failed: {ex.Message}");
                }
                finally
                {
                    if (!cancelled)
                    {
                        setIsChecking(false);
                    }
                }
            }

            ProbeHealthz();
            return () => cancelled = true;
        }, serverUrl);

        bool restartRequired = normalizedServerUrl.Length > 0
            && !string.Equals(normalizedServerUrl, liveRuntimeServerUrl, StringComparison.OrdinalIgnoreCase);

        return VStack(10,
            TextBlock("Server URL").FontSize(18).Bold(),
            HintText("Bind WinUI to the hosted controlface origin used for /sse, /mcp, and manage-boards traffic."),
            VStack(4,
                TextBlock("Server").Bold().Opacity(0.82),
                HStack(8,
                    TextBox(serverUrl, setServerUrl)
                        .AutomationName("Server URL")
                        .Set(textBox => textBox.TextWrapping = TextWrapping.Wrap)
                        .Flex(grow: 1),
                    BuildReachabilityAdornment(isChecking, isReachable))
            ),
            restartRequired
                ? (Element)TextBlock($"Saved. Restart to rebind the live board session from {liveRuntimeServerUrl} to {normalizedServerUrl}.")
                    .Opacity(0.68)
                    .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)
                : TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed),
            string.IsNullOrWhiteSpace(healthError)
                ? TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed)
                : TextBlock(healthError)
                    .Foreground(theme.StatusError)
                    .Opacity(0.88)
                    .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords));
    }

    private static Element BuildReachabilityAdornment(bool isChecking, bool isReachable)
    {
        if (isChecking)
        {
            return TextBlock("Checking...")
                    .AutomationName("Checking server reachability")
                .Opacity(0.68)
                .VerticalAlignment(VerticalAlignment.Center);
        }

        if (!isReachable)
        {
            return TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed);
        }

        return Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.Check2Circle, 16))
            .AutomationName("Server reachable");
    }

    private static Element HintText(string message)
    {
        return TextBlock(message)
            .Opacity(0.68)
            .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords);
    }

    private static string NormalizeForDisplay(string value)
    {
        return value.Trim().TrimEnd('/');
    }

    private static bool TryNormalizeServerUrl(string value, out Uri? serverBaseUri, out string error)
    {
        string candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? parsedUri)
            || (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            serverBaseUri = null;
            error = "Enter a valid absolute http or https server URL.";
            return false;
        }

        string normalized = parsedUri.GetLeftPart(UriPartial.Authority);
        if (!normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized += "/";
        }

        serverBaseUri = new Uri(normalized, UriKind.Absolute);
        error = string.Empty;
        return true;
    }
}