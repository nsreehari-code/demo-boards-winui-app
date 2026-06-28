namespace DemoBoards_WinUI.Config;

public static class WinUiServerUrlStore
{
    private const string ServerUrlProperty = "serverUrl";

    public static string? LoadOverride()
    {
        return WinUiBoardIdStore.LoadStringProperty(ServerUrlProperty);
    }

    public static void SaveOverride(string serverUrl)
    {
        WinUiBoardIdStore.SaveStringProperty(ServerUrlProperty, serverUrl);
    }
}