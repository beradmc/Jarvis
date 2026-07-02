using System;
using System.Text.Json;
using System.Threading.Tasks;
using JarvisCSharp.Memory;

namespace JarvisCSharp.Actions
{
    public class SaveMemoryAction : IAction
    {
        public string Name => "save_memory";
        public string Description => "Saves information about the user to persistent memory.";

        public Task<string> ExecuteAsync(string payload)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root     = doc.RootElement;
                var category = root.TryGetProperty("category", out var c) ? c.GetString() ?? "notes" : "notes";
                var key      = root.TryGetProperty("key",      out var k) ? k.GetString() ?? "" : "";
                var value    = root.TryGetProperty("value",    out var v) ? v.GetString() ?? "" : "";

                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                    return Task.FromResult("Hata: 'key' ve 'value' boş olamaz.");

                MemoryManager.UpdateMemory(category, key, value);
                Core.Logger.Information($"[Memory] Saved {category}/{key}");
                return Task.FromResult($"Hafızaya kaydedildi: {category}/{key} = {value}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Hata: Hafıza kaydı başarısız — {ex.Message}");
            }
        }
    }
}
