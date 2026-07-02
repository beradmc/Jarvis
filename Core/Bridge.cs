using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using JarvisCSharp.Services;

namespace JarvisCSharp.Core;

/// <summary>
/// Marshals JSON messages between the web UI and the C# backend services.
/// Protocol (web -> C#):  { "id": "<rpcId>", "action": "...", ...payload }
/// Protocol (C# -> web):  { "event": "...", ... }  for push events
///                         { "id": "<rpcId>", "ok": bool, "data"|"error": ... } for RPC replies
/// </summary>
public sealed class Bridge
{
    private readonly IBridgeHost _host;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public SystemShellService Shell { get; }
    public SystemInfoService SysInfo { get; }

    public Bridge(IBridgeHost host, SystemShellService shell, SystemInfoService sysInfo)
    {
        _host = host;
        Shell = shell;
        SysInfo = sysInfo;
    }

    public void PostToWeb(object payload)
        => _host.PostMessage(JsonSerializer.Serialize(payload, _json));

    public async Task HandleMessageAsync(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var action = root.TryGetProperty("action", out var actEl) ? actEl.GetString() : null;

            object? result = action switch
            {
                "sys.powershell" => await Shell.ExecutePowerShellCommandAsync(root.GetProperty("command").GetString()!),
                "sys.cmd" => await Shell.ExecuteCmdCommandAsync(root.GetProperty("command").GetString()!),
                "sys.getInfo" => SysInfo.GetSystemInfo("all"),

                _ => new { error = $"Unknown action: {action}" },
            };

            if (id != null)
            {
                PostToWeb(new { id, ok = true, data = result });
            }
        }
        catch (Exception ex)
        {
            var id = json.Contains("\"id\"") ? JsonDocument.Parse(json).RootElement.GetProperty("id").GetString() : null;
            PostToWeb(new { id, ok = false, error = ex.Message });
        }
    }
}
