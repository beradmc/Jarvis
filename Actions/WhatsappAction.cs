using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JarvisCSharp.Utils;
using JarvisCSharp.Core;
using JarvisCSharp.Memory;
using JarvisCSharp.Services.Automation;

namespace JarvisCSharp.Actions
{
    public class WhatsappAction : IAction
    {
        public string Name => "send_whatsapp_message";
        public string Description => "Opens WhatsApp and prepares or sends a message.";

        private readonly WhatsAppAutomationHelper _automationHelper;

        public WhatsappAction(WhatsAppAutomationHelper automationHelper)
        {
            _automationHelper = automationHelper;
        }

        // phone_book.json yolu — Python ile aynı
        private static readonly string PhoneBookPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "config", "phone_book.json");

        public async Task<string> ExecuteAsync(string payload)
        {
            try
            {
                var args = JsonSerializer.Deserialize<WhatsAppPayload>(payload);
                if (args == null)
                {
                    return await ExecuteLegacyFormat(payload);
                }

                var message = args.Message?.Trim() ?? "";
                var recipientName = args.RecipientName?.Trim() ?? "";
                var phoneNumber = args.PhoneNumber?.Trim() ?? "";

                if (string.IsNullOrEmpty(recipientName) && string.IsNullOrEmpty(phoneNumber))
                {
                    return "WhatsApp mesajı için kişi adı veya telefon numarası gerekli.";
                }

                // Try to resolve name from phone book for fallback, but we'll prioritize deep UI automation by name
                if (string.IsNullOrEmpty(recipientName) && !string.IsNullOrEmpty(phoneNumber))
                {
                    // If we only have phone number, use it as name for search or we could use deep link.
                    recipientName = phoneNumber; 
                }

                // For Task 19 deep integration, we find contact by name and send message
                var result = await _automationHelper.SendMessageAsync(recipientName, message);
                if (result.Success)
                {
                    return result.Message;
                }
                
                return $"WhatsApp işleminde hata oluştu: {result.Message}";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "WhatsApp action failed");
                return $"Hata: WhatsApp açılamadı — {ex.Message}";
            }
        }

        private async Task<string> ExecuteLegacyFormat(string payload)
        {
            var parts = payload.Split(':', 2);
            if (parts.Length < 2)
            {
                await _automationHelper.OpenWhatsAppAsync();
                return "WhatsApp açıldı.";
            }

            var phoneOrName = parts[0].Trim();
            var message = parts[1].Trim();

            var result = await _automationHelper.SendMessageAsync(phoneOrName, message);
            return result.Message;
        }

        private class WhatsAppPayload
        {
            public string? Message { get; set; }
            public string? PhoneNumber { get; set; }
            public string? RecipientName { get; set; }
            public bool SendNow { get; set; }
            public string? AppTarget { get; set; }
        }

        /// <summary>
        /// Hem memory.json (whatsapp_contacts) hem phone_book.json'dan kişi arar.
        /// Kişi adı ve normalizasyon Python'daki _find_contact mantığıyla uyumlu.
        /// </summary>
        public static string? FindPhoneByName(string recipientName)
        {
            if (string.IsNullOrWhiteSpace(recipientName)) return null;
            var needle = NormalizeLookup(recipientName);

            // 1) memory.json → whatsapp_contacts
            var memory = MemoryManager.LoadMemory();
            if (memory.TryGetPropertyValue("whatsapp_contacts", out var contacts) &&
                contacts is JsonObject contactsObj)
            {
                var phone = SearchContacts(contactsObj, needle);
                if (phone != null) return phone;
            }

            // 2) phone_book.json
            try
            {
                if (File.Exists(PhoneBookPath))
                {
                    var json = File.ReadAllText(PhoneBookPath);
                    if (JsonNode.Parse(json) is JsonObject pb)
                    {
                        var phone = SearchContacts(pb, needle);
                        if (phone != null) return phone;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"[WhatsApp] phone_book.json read failed: {ex.Message}");
            }

            return null;
        }

        private static string? SearchContacts(JsonObject contacts, string needle)
        {
            int bestScore = 0;
            string? bestPhone = null;

            foreach (var kvp in contacts)
            {
                if (kvp.Value is not JsonObject entry) continue;

                // İsim adayları
                var names = new List<string> { kvp.Key };
                if (entry.TryGetPropertyValue("display_name", out var dn)) names.Add(dn?.ToString() ?? "");
                if (entry.TryGetPropertyValue("aliases", out var aliasNode))
                {
                    if (aliasNode is JsonArray arr)
                        foreach (var a in arr) names.Add(a?.ToString() ?? "");
                    else
                        names.Add(aliasNode?.ToString() ?? "");
                }

                // Telefon numarasını bul
                string phone = "";
                if (entry.TryGetPropertyValue("value", out var v))
                    phone = v?.ToString() ?? "";
                else if (entry.TryGetPropertyValue("phone_number", out var pn))
                    phone = pn?.ToString() ?? "";

                if (string.IsNullOrEmpty(phone)) continue;

                // En iyi eşleşmeyi bul
                foreach (var name in names)
                {
                    int score = MatchScore(needle, NormalizeLookup(name));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPhone = phone;
                    }
                }
            }

            return bestScore > 0 ? bestPhone : null;
        }

        private static int MatchScore(string needle, string candidate)
        {
            if (string.IsNullOrEmpty(candidate)) return 0;
            if (candidate == needle)              return 300;
            if (candidate.StartsWith(needle) || needle.StartsWith(candidate)) return 220;
            if (candidate.Contains(needle))       return 160;

            var needleParts = needle.Split(' ');
            bool allParts = needleParts.Length > 0;
            foreach (var p in needleParts)
                if (!candidate.Contains(p)) { allParts = false; break; }
            if (allParts && needleParts.Length > 0) return 120;

            return 0;
        }

        // Telefon numarasını uluslararası formata normalize et (Python ile aynı mantık)
        public static string NormalizePhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return "";
            var digits = Regex.Replace(phone, @"\D+", "");
            if (digits.Length == 11 && digits.StartsWith("0"))
                digits = "90" + digits[1..];
            else if (digits.Length == 10)
                digits = "90" + digits;
            if (digits.Length < 8 || digits.Length > 15) return "";
            return digits;
        }

        private static string NormalizeLookup(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            
            // Lowercase ve Unicode normalizasyon
            text = text.Trim().ToLowerInvariant();
            
            // NFKD normalizasyon ile diacritic'leri ayır
            text = text.Normalize(System.Text.NormalizationForm.FormKD);
            
            // Combining karakterleri kaldır (diacritic marks)
            var chars = new List<char>();
            foreach (var ch in text)
            {
                var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
                    chars.Add(ch);
            }
            text = new string(chars.ToArray());
            
            // Türkçe karakter dönüşümleri
            text = text.Replace("ı", "i").Replace("ğ", "g").Replace("ü", "u")
                       .Replace("ş", "s").Replace("ö", "o").Replace("ç", "c");
            
            // Whitespace'leri normalize et
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        /// <summary>
        /// WhatsApp kişisini memory.json'a kaydeder.
        /// </summary>
        public static string SaveWhatsappContact(string displayName, string phoneNumber, string aliases = "")
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return "Kişi adı boş olamaz.";

            var normalized = NormalizePhone(phoneNumber);
            if (string.IsNullOrEmpty(normalized))
                return "Telefon numarası uluslararası formatta olmalı. Örn: +905551112233";

            var aliasList = new List<string>();
            if (!string.IsNullOrWhiteSpace(aliases))
            {
                var parts = aliases.Split(',');
                foreach (var p in parts)
                {
                    var trimmed = p.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        aliasList.Add(trimmed);
                }
            }

            var memory = MemoryManager.LoadMemory();
            if (!memory.ContainsKey("whatsapp_contacts"))
                memory["whatsapp_contacts"] = new JsonObject();

            if (memory["whatsapp_contacts"] is not JsonObject contacts)
            {
                contacts = new JsonObject();
                memory["whatsapp_contacts"] = contacts;
            }

            var key = Regex.Replace(NormalizeLookup(displayName), @"[^a-z0-9]+", "_").Trim('_');
            if (string.IsNullOrEmpty(key)) key = "contact";

            var contactObj = new JsonObject
            {
                ["value"] = $"+{normalized}",
                ["display_name"] = displayName.Trim()
            };

            if (aliasList.Count > 0)
            {
                var aliasArray = new JsonArray();
                foreach (var alias in aliasList)
                    aliasArray.Add(alias);
                contactObj["aliases"] = aliasArray;
            }

            contacts[key] = contactObj;

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var memoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "memory.json");
            var dir = Path.GetDirectoryName(memoryPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(memoryPath, memory.ToJsonString(options));

            Logger.Information($"[WhatsApp] Contact saved: {displayName} (+{normalized})");

            if (aliasList.Count > 0)
                return $"{displayName.Trim()} WhatsApp kişilerine kaydedildi. Takma adlar: {string.Join(", ", aliasList)}";
            
            return $"{displayName.Trim()} WhatsApp kişilerine kaydedildi.";
        }
    }
}
