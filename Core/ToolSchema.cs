using System.Collections.Generic;
using Google.GenAI.Types;

namespace JarvisCSharp.Core
{
    /// <summary>
    /// Gemini Live API için tool/function declaration şemaları.
    /// Python'daki core/tools_schema.py'nin C# karşılığı.
    /// </summary>
    public static class ToolSchema
    {
        public static List<FunctionDeclaration> GetDeclarations()
        {
            return new List<FunctionDeclaration>
            {
                MakeFunc("open_app",
                    "Windows'ta herhangi bir uygulamayı açar. Spotify, Chrome, Terminal, VS Code vb.",
                    new[] { "app_name" },
                    ("app_name", GT.String, "Uygulama adı (örn. 'Spotify', 'Chrome', 'Terminal')")),

                MakeFunc("sys_info",
                    "Sistem bilgisi alır: pil durumu, CPU, RAM, disk, saat, tarih, ağ bağlantısı.",
                    new[] { "query" },
                    ("query", GT.String, "battery | cpu | ram | disk | time | date | network | all")),

                MakeFunc("get_weather",
                    "Anlık hava durumunu özetler. Varsayılan İstanbul.",
                    new string[0],
                    ("location", GT.String, "Şehir veya konum. Boş bırakılırsa İstanbul kullanılır.")),

                MakeFunc("get_health_data",
                    "iPhone sağlık verilerini okur: adım, nabız, HRV, uyku, kalori, egzersiz vb.",
                    new[] { "query" },
                    ("query", GT.String, "all | heart | steps | sleep | oxygen | walking | dün | bugün")),

                MakeFunc("get_calendar_events",
                    "Google Calendar etkinliklerini açar ve özetler.",
                    new[] { "query" },
                    ("query",  GT.String,  "today | tomorrow | next | agenda | week"),
                    ("limit",  GT.Number,  "Maksimum etkinlik sayısı")),

                MakeFunc("add_calendar_event",
                    "Google Calendar'da yeni etkinlik oluşturma ekranını hazırlar.",
                    new[] { "title", "start_iso" },
                    ("title",         GT.String,  "Etkinlik başlığı"),
                    ("start_iso",     GT.String,  "Başlangıç tarih/saat ISO formatında"),
                    ("end_iso",       GT.String,  "Bitiş tarih/saat"),
                    ("location",      GT.String,  "Konum"),
                    ("notes",         GT.String,  "Notlar"),
                    ("calendar_name", GT.String,  "Takvim adı"),
                    ("all_day",       GT.Boolean, "Tüm gün etkinliği ise true")),

                MakeFunc("delete_calendar_event",
                    "Google Calendar'ı açar. Silme işlemi tarayıcıda tamamlanır.",
                    new[] { "title" },
                    ("title",              GT.String,  "Silinecek etkinlik başlığı"),
                    ("start_iso",          GT.String,  "Tarih/saat"),
                    ("calendar_name",      GT.String,  "Takvim adı"),
                    ("delete_all_matches", GT.Boolean, "Tüm eşleşenleri sil")),

                MakeFunc("get_reminders",
                    "Microsoft To-Do hatırlatıcılarını açar ve listeler.",
                    new[] { "query" },
                    ("query",     GT.String, "today | upcoming | overdue | all"),
                    ("limit",     GT.Number, "Maksimum sayı"),
                    ("list_name", GT.String, "Liste adı")),

                MakeFunc("add_reminder",
                    "Microsoft To-Do'ya yeni hatırlatıcı ekler.",
                    new[] { "title" },
                    ("title",     GT.String,  "Hatırlatıcı başlığı"),
                    ("due_iso",   GT.String,  "Tarih/saat ISO formatında"),
                    ("notes",     GT.String,  "Not"),
                    ("list_name", GT.String,  "Liste adı"),
                    ("priority",  GT.String,  "low | medium | high"),
                    ("all_day",   GT.Boolean, "Tüm gün")),

                MakeFunc("browser_control",
                    "Tarayıcıda URL açar veya Google/YouTube'da arama yapar.",
                    new[] { "action" },
                    ("action", GT.String, "open_url | search | play_youtube"),
                    ("url",    GT.String, "Açılacak URL (open_url için)"),
                    ("query",  GT.String, "Arama sorgusu (search veya play_youtube için)")),

                MakeFunc("shell_run",
                    "Windows komut satırı komutu çalıştırır.",
                    new[] { "command" },
                    ("command", GT.String, "Çalıştırılacak komut")),

                MakeFunc("play_media",
                    "YouTube veya Spotify'da şarkı, müzik veya video açar.",
                    new[] { "query" },
                    ("query",    GT.String,  "Şarkı, sanatçı veya video arama ifadesi"),
                    ("provider", GT.String,  "auto | youtube | spotify"),
                    ("autoplay", GT.Boolean, "true ise mümkünse doğrudan oynatır")),

                MakeFunc("get_youtube_channel_report",
                    "YouTube kanalının istatistiklerini ve son video performansını raporlar.",
                    new[] { "query" },
                    ("query",       GT.String, "Analiz isteği (örn. 'istatistiklerim nasıl')"),
                    ("handle",      GT.String, "Kanal handle'ı veya URL'si"),
                    ("video_limit", GT.Number, "Son kaç video analiz edilsin (varsayılan 6)")),

                MakeFunc("analyze_screen",
                    "Ekran görüntüsü alıp Gemini vision ile analiz eder.",
                    new[] { "query" },
                    ("query",  GT.String, "Ekran sorusu"),
                    ("target", GT.String, "active_window | primary_monitor | all_monitors")),

                MakeFunc("save_memory",
                    "Kullanıcı hakkında önemli bilgiyi kalıcı belleğe kaydeder. İsim, tercihler vb.",
                    new[] { "category", "key", "value" },
                    ("category", GT.String, "identity | preferences | projects | notes"),
                    ("key",      GT.String, "Kısa anahtar (örn. 'name')"),
                    ("value",    GT.String, "Değer")),

                MakeFunc("delete_memory",
                    "Kalıcı hafızadaki bir kaydı siler.",
                    new string[0],
                    ("category",   GT.String, "Kayıt kategorisi"),
                    ("key",        GT.String, "Silinecek anahtar"),
                    ("match_text", GT.String, "Kaydı bulmak için arama metni")),

                MakeFunc("send_whatsapp_message",
                    "WhatsApp üzerinden mesaj taslağı açar veya gönderir.",
                    new[] { "message" },
                    ("recipient_name", GT.String,  "Kişi adı"),
                    ("phone_number",   GT.String,  "Uluslararası telefon numarası"),
                    ("message",        GT.String,  "Gönderilecek mesaj"),
                    ("app_target",     GT.String,  "desktop | web | auto"),
                    ("send_now",       GT.Boolean, "true ise otomatik gönderir")),

                MakeFunc("save_whatsapp_contact",
                    "WhatsApp kişisini adı ve telefon numarasıyla belleğe kaydeder.",
                    new[] { "display_name", "phone_number" },
                    ("display_name", GT.String, "Kişi adı"),
                    ("phone_number", GT.String, "Uluslararası telefon numarası"),
                    ("aliases",      GT.String, "Virgülle ayrılmış alternatif isimler")),

                MakeFunc("clipboard_action",
                    "Pano işlemleri: metni oku, yaz veya AI ile işle (çevir, özetle vb.).",
                    new[] { "action" },
                    ("action",      GT.String, "read | write | smart"),
                    ("text",        GT.String, "write eylemi için panoya yazılacak metin"),
                    ("instruction", GT.String, "smart eylemi için AI talimatı (çevir, özetle vb.)")),

                MakeFunc("desktop_control",
                    "Masaüstü otomasyonu: ses, medya, pencere, fare, klavye, kilit, uyku.",
                    new[] { "action" },
                    ("action", GT.String,
                        "volume_up | volume_down | volume_mute | volume_set | " +
                        "media_play_pause | media_next | media_prev | media_stop | " +
                        "lock_screen | sleep | close_window | minimize_window | maximize_window | " +
                        "hotkey | type_text | mouse_click | mouse_move | mouse_scroll | screenshot"),
                    ("value", GT.String, "Eyleme bağlı değer (ses seviyesi, koordinat, metin vb.)")),

                MakeFunc("change_mode",
                    "Jarvis'in çalışma modunu değiştirir. Kullanıcı 'kapan, sus, sessiz ol' derse muted, 'kapat, uyu' derse passive yap.",
                    new[] { "mode" },
                    ("mode", GT.String, "muted | passive")),

                MakeFunc("window_control",
                    "Pencere yönetimi aracı. Arka plandaki uygulamaları öne getirmek veya açık pencereleri görmek için kullanılır.",
                    new[] { "payload" },
                    ("payload", GT.String, "list | focus:PencereAdi | close:PencereAdi")),

                MakeFunc("ui_control",
                    "Akıllı Arayüz Otomasyonu: Aktif penceredeki öğeleri bulur, tıklar veya metin yazar (Koordinatsız). 'analyze', 'click:ÖğeAdı', 'type:ÖğeAdı,Metin' formatında kullanılır.",
                    new[] { "payload" },
                    ("payload", GT.String, "analyze | click:OgeAdi | type:OgeAdi,Metin")),

                // --- Advanced Window & Automation Controls ---
                MakeFunc("click_element",
                    "Ekranda veya aktif pencerede belirtilen UI öğesine tıklar.",
                    new[] { "elementName" },
                    ("elementName", GT.String, "Tıklanacak öğenin adı (örn: Gönder butonu)"),
                    ("clickType", GT.String, "LeftSingle | RightSingle | LeftDouble"),
                    ("targetWindow", GT.String, "Opsiyonel hedef pencere başlığı")),

                MakeFunc("type_text",
                    "Bir UI öğesine metin yazar.",
                    new[] { "text" },
                    ("text", GT.String, "Yazılacak metin"),
                    ("targetElement", GT.String, "Hedef öğe adı (boşsa aktif konuma yazar)"),
                    ("clearExisting", GT.Boolean, "Mevcut metni temizle")),

                MakeFunc("send_shortcut",
                    "Klavye kısayolu gönderir (örn: Ctrl+C, Alt+Tab).",
                    new[] { "shortcut" },
                    ("shortcut", GT.String, "Kısayol tuşu"),
                    ("targetWindow", GT.String, "Opsiyonel hedef pencere")),

                MakeFunc("multi_step_workflow",
                    "Doğal dil komutunu analiz edip çok adımlı bir otomasyon iş akışı çalıştırır.",
                    new[] { "command" },
                    ("command", GT.String, "Örn: 'Not defterini aç ve merhaba yaz'")),

                MakeFunc("extract_text",
                    "Ekrandan veya belirtilen alandan metin (OCR) okur.",
                    new string[0],
                    ("region", GT.String, "Okunacak bölge (boşsa tüm ekran)"),
                    ("language", GT.String, "tr | en")),

                MakeFunc("launch_app",
                    "Sistemde yüklü bir uygulamayı isminden bularak başlatır.",
                    new[] { "appName" },
                    ("appName", GT.String, "Uygulama adı (örn: notepad, spotify)")),

                MakeFunc("switch_window",
                    "Belirtilen başlığa sahip pencereyi öne getirip odaklar.",
                    new[] { "windowTitle" },
                    ("windowTitle", GT.String, "Pencere başlığının bir kısmı")),

                MakeFunc("maximize_window", "Aktif pencereyi tam ekran yapar.", new string[0]),
                MakeFunc("minimize_window", "Aktif pencereyi küçültür.", new string[0]),
                MakeFunc("close_window", "Aktif pencereyi kapatır.", new string[0]),

                MakeFunc("move_window",
                    "Aktif pencereyi belirtilen monitöre taşır.",
                    new[] { "monitorIndex" },
                    ("monitorIndex", GT.Number, "Hedef monitör indeksi (0, 1 vb.)")),

                MakeFunc("correct_detection",
                    "Yanlış tespit edilen bir UI öğesini düzeltmek için öğrenme sistemine kaydeder.",
                    new[] { "appName", "elementName", "correctLocation" },
                    ("appName", GT.String, "Uygulama adı"),
                    ("elementName", GT.String, "Öğe tanımı"),
                    ("correctLocation", GT.String, "Doğru konum veya işaretçi")),

                MakeFunc("learn_alias",
                    "Kullanıcının kendine has kelimelerini asıl işleme eşler. (Örn: 'temizle' -> 'delete')",
                    new[] { "alias", "canonicalName" },
                    ("alias", GT.String, "Kullanıcının kullandığı yeni ifade (örn: uçur, temizle)"),
                    ("canonicalName", GT.String, "Sistemin tanıdığı gerçek eylem veya hedef (örn: delete)"),
                    ("appName", GT.String, "Opsiyonel. Sadece belirli bir uygulama içinse uygulama adı")),
            };
        }

        // ── Builder ───────────────────────────────────────────────────────────

        // GT = kısaltma
        private static class GT
        {
            public static readonly Google.GenAI.Types.Type String  = Google.GenAI.Types.Type.String;
            public static readonly Google.GenAI.Types.Type Number  = Google.GenAI.Types.Type.Number;
            public static readonly Google.GenAI.Types.Type Boolean = Google.GenAI.Types.Type.Boolean;
        }

        private static FunctionDeclaration MakeFunc(
            string name,
            string description,
            string[] required,
            params (string name, Google.GenAI.Types.Type type, string desc)[] props)
        {
            var schema = new Schema
            {
                Type       = Google.GenAI.Types.Type.Object,
                Properties = new System.Collections.Generic.Dictionary<string, Schema>()
            };

            if (required != null && required.Length > 0)
            {
                schema.Required = new System.Collections.Generic.List<string>();
                foreach (var r in required)
                    schema.Required.Add(r);
            }

            foreach (var (pName, pType, pDesc) in props)
            {
                schema.Properties[pName] = new Schema { Type = pType, Description = pDesc };
            }

            return new FunctionDeclaration
            {
                Name        = name,
                Description = description,
                Parameters  = schema
            };
        }
    }
}
