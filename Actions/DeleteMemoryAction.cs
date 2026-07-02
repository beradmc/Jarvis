using System;
using System.Text.Json;
using System.Threading.Tasks;
using JarvisCSharp.Memory;

namespace JarvisCSharp.Actions
{
    public class DeleteMemoryAction : IAction
    {
        public string Name => "delete_memory";
        public string Description => "Deletes information from persistent memory.";

        public Task<string> ExecuteAsync(string payload)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root     = doc.RootElement;
                var category = root.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                var key      = root.TryGetProperty("key",      out var k) ? k.GetString() ?? "" : "";

                if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(key))
                    return Task.FromResult("Hata: 'category' ve 'key' belirtilmelidir.");

                var result = MemoryManager.DeleteMemory(category, key);
                Core.Logger.Information($"[Memory] Deleted {category}/{key}");
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Hata: Hafıza silme başarısız — {ex.Message}");
            }
        }
    }
}
