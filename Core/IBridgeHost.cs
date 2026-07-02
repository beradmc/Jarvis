namespace JarvisCSharp.Core;

/// <summary>
/// Platform-specific host that the Bridge uses to communicate with the web UI.
/// </summary>
public interface IBridgeHost
{
    void PostMessage(string json);
    void NavigateReload();
    void ToggleDevTools();
    void SetZoom(double zoom);
    void BringToFront();
    void CloseApp();
}
