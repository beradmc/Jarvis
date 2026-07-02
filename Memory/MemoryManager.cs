using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using JarvisCSharp.Core;

namespace JarvisCSharp.Memory
{
    public static class MemoryManager
    {
        private static readonly string MemoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "memory.json");
        private static readonly object _lock = new object();

        public static JsonObject LoadMemory()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(MemoryPath))
                    {
                        var json = File.ReadAllText(MemoryPath);
                        var node = JsonNode.Parse(json);
                        if (node is JsonObject obj)
                            return obj;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to load memory.json");
                }
                return new JsonObject();
            }
        }

        private static void SaveMemory(JsonObject memory)
        {
            lock (_lock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(MemoryPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var options = new JsonSerializerOptions 
                    { 
                        WriteIndented = true, 
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                    };
                    File.WriteAllText(MemoryPath, memory.ToJsonString(options));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to save memory.json");
                }
            }
        }

        public static void UpdateMemory(string category, string key, string value)
        {
            var memory = LoadMemory();
            
            if (!memory.ContainsKey(category))
            {
                memory[category] = new JsonObject();
            }
            
            if (memory[category] is JsonObject categoryObj)
            {
                var itemObj = new JsonObject();
                itemObj["value"] = value;
                categoryObj[key] = itemObj;
            }
            
            SaveMemory(memory);
            Logger.Information($"[Memory] Saved {category}/{key} = {value}");
        }

        public static string DeleteMemory(string category, string key)
        {
            var memory = LoadMemory();
            
            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(key))
                return "Silmek için category ve key gerekli.";

            if (memory.ContainsKey(category) && memory[category] is JsonObject categoryObj)
            {
                if (categoryObj.ContainsKey(key))
                {
                    categoryObj.Remove(key);
                    if (categoryObj.Count == 0)
                        memory.Remove(category);
                        
                    SaveMemory(memory);
                    Logger.Information($"[Memory] Deleted {category}/{key}");
                    return $"{category}/{key} hafızadan kaldırıldı.";
                }
            }
            
            return "Bu hafıza kaydını bulamadım.";
        }

        public static string FormatMemoryForPrompt()
        {
            var memory = LoadMemory();
            if (memory.Count == 0) return "";

            var lines = new List<string> { "[KULLANICI HAKKINDA BİLGİLER]" };

            foreach (var kvp in memory)
            {
                var category = kvp.Key;
                var items = kvp.Value;

                if (items is JsonObject itemsObj)
                {
                    foreach (var itemKvp in itemsObj)
                    {
                        var key = itemKvp.Key;
                        var val = itemKvp.Value;
                        
                        string valueStr = val?.ToString() ?? "";
                        if (val is JsonObject valObj && valObj.ContainsKey("value"))
                        {
                            valueStr = valObj["value"]?.ToString() ?? "";
                        }
                        
                        lines.Add($"  {category}/{key}: {valueStr}");
                    }
                }
                else
                {
                    lines.Add($"  {category}: {items?.ToString()}");
                }
            }

            return string.Join("\n", lines);
        }
    }
}
