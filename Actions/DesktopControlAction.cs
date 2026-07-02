using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WindowsInput;
using WindowsInput.Native;
using JarvisCSharp.Core;

namespace JarvisCSharp.Actions
{
    public class DesktopControlAction : IAction
    {
        public string Name => "desktop_control";
        public string Description => "Desktop automation: volume, window, mouse (kör koordinat - prefer ui_control if possible), keyboard, etc.";

        private readonly IInputSimulator _sim = new InputSimulator();

        public Task<string> ExecuteAsync(string payload)
        {
            var parts  = payload.Split(':', 2);
            var action = parts[0].Trim().ToLower();
            var value  = parts.Length > 1 ? parts[1].Trim() : "";

            try
            {
                var result = action switch
                {
                    // ── Ses ───────────────────────────────────────────────────
                    "volume_up"         => VolumeStep(true),
                    "volume_down"       => VolumeStep(false),
                    "volume_mute"       => KeyTap(VirtualKeyCode.VOLUME_MUTE, "Ses kapatıldı/açıldı."),
                    "volume_set"        => SetVolume(value),

                    // ── Medya ─────────────────────────────────────────────────
                    "media_play_pause"  => KeyTap(VirtualKeyCode.MEDIA_PLAY_PAUSE, "Oynat/duraklat."),
                    "media_next"        => KeyTap(VirtualKeyCode.MEDIA_NEXT_TRACK, "Sonraki parça."),
                    "media_prev"        => KeyTap(VirtualKeyCode.MEDIA_PREV_TRACK, "Önceki parça."),
                    "media_stop"        => KeyTap(VirtualKeyCode.MEDIA_STOP, "Medya durduruldu."),

                    // ── Pencere ───────────────────────────────────────────────
                    "close_window"      => ModKey(VirtualKeyCode.LMENU, VirtualKeyCode.F4, "Pencere kapatıldı."),
                    "minimize_window"   => MinimizeActive(),
                    "maximize_window"   => MaximizeActive(),

                    // ── Sistem ────────────────────────────────────────────────
                    "lock_screen"       => LockScreen(),
                    "sleep"             => SleepPC(),
                    "screenshot"        => TakeScreenshot(),

                    // ── Klavye ───────────────────────────────────────────────
                    "hotkey"            => SendHotkey(value),
                    "type_text"         => TypeText(value),

                    // ── Fare ──────────────────────────────────────────────────
                    "mouse_click"       => MouseClick(value),
                    "mouse_move"        => MouseMove(value),
                    "mouse_scroll"      => MouseScroll(value),
                    "mouse_drag"        => MouseDrag(value),

                    _                   => $"Bilinmeyen eylem: {action}"
                };

                Logger.Information($"[Desktop] {action} → {result}");
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Desktop action failed: {action}");
                return Task.FromResult($"Hata: {action} başarısız — {ex.Message}");
            }
        }

        // ── Ses ──────────────────────────────────────────────────────────────

        private string VolumeStep(bool up)
        {
            var key = up ? VirtualKeyCode.VOLUME_UP : VirtualKeyCode.VOLUME_DOWN;
            for (int i = 0; i < 2; i++) _sim.Keyboard.KeyPress(key);
            return up ? "Ses artırıldı." : "Ses azaltıldı.";
        }

        private static string SetVolume(string value)
        {
            if (!int.TryParse(value, out var level)) return "Geçersiz ses seviyesi.";
            level = Math.Clamp(level, 0, 100);
            // Windows Core Audio API aracılığıyla ses seviyesi ayarla
            try
            {
                SetMasterVolume(level / 100.0f);
                return $"Ses seviyesi %{level} olarak ayarlandı.";
            }
            catch
            {
                return $"Ses seviyesi ayarlanamadı.";
            }
        }

        // ── Pencere ──────────────────────────────────────────────────────────

        private string MinimizeActive()
        {
            _sim.Keyboard.ModifiedKeyStroke(VirtualKeyCode.LWIN, VirtualKeyCode.VK_D);
            return "Pencere küçültüldü.";
        }

        private string MaximizeActive()
        {
            _sim.Keyboard.ModifiedKeyStroke(VirtualKeyCode.LWIN, VirtualKeyCode.UP);
            return "Pencere büyütüldü.";
        }

        // ── Sistem ───────────────────────────────────────────────────────────

        private static string LockScreen()
        {
            LockWorkStation();
            return "Ekran kilitlendi.";
        }

        private static string SleepPC()
        {
            SetSuspendState(false, true, true);
            return "Bilgisayar uyku moduna alındı.";
        }

        private static string TakeScreenshot()
        {
            try
            {
                int w = GetSystemMetrics(78); // SM_CXVIRTUALSCREEN
                int h = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
                int x = GetSystemMetrics(76); // SM_XVIRTUALSCREEN
                int y = GetSystemMetrics(77); // SM_YVIRTUALSCREEN
                if (w <= 0) { w = 1920; h = 1080; }

                using var bmp = new System.Drawing.Bitmap(w, h);
                using var g   = System.Drawing.Graphics.FromImage(bmp);
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var path    = Path.Combine(desktop, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                return $"Ekran görüntüsü kaydedildi: {path}";
            }
            catch (Exception ex)
            {
                return $"Ekran görüntüsü alınamadı: {ex.Message}";
            }
        }

        // ── Klavye ───────────────────────────────────────────────────────────

        private string SendHotkey(string combo)
        {
            if (string.IsNullOrWhiteSpace(combo)) return "Kısayol belirtilmedi.";

            var parts = combo.ToLower().Split('+');
            var modifiers = new System.Collections.Generic.List<VirtualKeyCode>();
            VirtualKeyCode? mainKey = null;

            foreach (var part in parts)
            {
                switch (part.Trim())
                {
                    case "ctrl":  case "control": modifiers.Add(VirtualKeyCode.CONTROL); break;
                    case "alt":   modifiers.Add(VirtualKeyCode.MENU); break;
                    case "shift": modifiers.Add(VirtualKeyCode.SHIFT); break;
                    case "win":   case "windows": modifiers.Add(VirtualKeyCode.LWIN); break;
                    default:
                        mainKey = ParseKey(part.Trim());
                        break;
                }
            }

            if (mainKey == null) return "Geçersiz kısayol.";

            if (modifiers.Count > 0)
                _sim.Keyboard.ModifiedKeyStroke(modifiers, new[] { mainKey.Value });
            else
                _sim.Keyboard.KeyPress(mainKey.Value);

            return $"Kısayol gönderildi: {combo}";
        }

        private string TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "Yazılacak metin boş.";
            _sim.Keyboard.TextEntry(text);
            return $"Metin yazıldı: {text[..Math.Min(40, text.Length)]}";
        }

        // ── Fare ─────────────────────────────────────────────────────────────

        private string MouseClick(string value)
        {
            // Formatlar: "left", "right", "double", "500,400", "right:500,400", "double:500,400"
            if (string.IsNullOrWhiteSpace(value) || value == "left")
            {
                _sim.Mouse.LeftButtonClick();
                return "Sol tıklandı.";
            }

            var lower = value.ToLower();
            var isRight  = lower.StartsWith("right");
            var isDouble = lower.StartsWith("double");

            // Koordinat varsa fareyi oraya taşı
            var coordPart = lower.Replace("right:", "").Replace("double:", "").Replace("left:", "").Trim();
            if (coordPart.Contains(','))
            {
                var xy = coordPart.Split(',');
                if (xy.Length == 2 && int.TryParse(xy[0].Trim(), out var cx) && int.TryParse(xy[1].Trim(), out var cy))
                    SetCursorPos(cx, cy);
            }

            if (isRight)        { _sim.Mouse.RightButtonClick(); return "Sağ tıklandı."; }
            if (isDouble)       { _sim.Mouse.LeftButtonDoubleClick(); return "Çift tıklandı."; }
            _sim.Mouse.LeftButtonClick();
            return "Sol tıklandı.";
        }

        private string MouseMove(string value)
        {
            if (!TryParseCoords(value, out var x, out var y)) return "Geçersiz koordinat.";
            SetCursorPos(x, y);
            return $"Fare taşındı: ({x}, {y})";
        }

        private string MouseScroll(string value)
        {
            if (!int.TryParse(value, out var amount)) amount = -3;
            _sim.Mouse.VerticalScroll(amount);
            return amount > 0 ? $"Yukarı kaydırıldı ({amount})." : $"Aşağı kaydırıldı ({Math.Abs(amount)}).";
        }

        private string MouseDrag(string value)
        {
            if (!TryParseCoords(value, out var tx, out var ty)) return "Geçersiz hedef koordinat.";
            _sim.Mouse.LeftButtonDown();
            System.Threading.Thread.Sleep(80);
            SetCursorPos(tx, ty);
            System.Threading.Thread.Sleep(80);
            _sim.Mouse.LeftButtonUp();
            return $"Sürükleme tamamlandı: ({tx}, {ty})";
        }

        // ── Yardımcı ─────────────────────────────────────────────────────────

        private string KeyTap(VirtualKeyCode key, string msg)
        {
            _sim.Keyboard.KeyPress(key);
            return msg;
        }

        private string ModKey(VirtualKeyCode mod, VirtualKeyCode key, string msg)
        {
            _sim.Keyboard.ModifiedKeyStroke(mod, key);
            return msg;
        }

        private static bool TryParseCoords(string value, out int x, out int y)
        {
            x = 0; y = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var parts = value.Split(',');
            return parts.Length == 2 && int.TryParse(parts[0].Trim(), out x) && int.TryParse(parts[1].Trim(), out y);
        }

        private static VirtualKeyCode ParseKey(string key) => key switch
        {
            "tab"    => VirtualKeyCode.TAB,
            "enter"  => VirtualKeyCode.RETURN,
            "esc"    => VirtualKeyCode.ESCAPE,
            "space"  => VirtualKeyCode.SPACE,
            "up"     => VirtualKeyCode.UP,
            "down"   => VirtualKeyCode.DOWN,
            "left"   => VirtualKeyCode.LEFT,
            "right"  => VirtualKeyCode.RIGHT,
            "del"    => VirtualKeyCode.DELETE,
            "backspace" => VirtualKeyCode.BACK,
            "home"   => VirtualKeyCode.HOME,
            "end"    => VirtualKeyCode.END,
            "pgup"   => VirtualKeyCode.PRIOR,
            "pgdn"   => VirtualKeyCode.NEXT,
            "f1"     => VirtualKeyCode.F1,
            "f2"     => VirtualKeyCode.F2,
            "f3"     => VirtualKeyCode.F3,
            "f4"     => VirtualKeyCode.F4,
            "f5"     => VirtualKeyCode.F5,
            "f6"     => VirtualKeyCode.F6,
            "f7"     => VirtualKeyCode.F7,
            "f8"     => VirtualKeyCode.F8,
            "f9"     => VirtualKeyCode.F9,
            "f10"    => VirtualKeyCode.F10,
            "f11"    => VirtualKeyCode.F11,
            "f12"    => VirtualKeyCode.F12,
            "a" => VirtualKeyCode.VK_A, "b" => VirtualKeyCode.VK_B, "c" => VirtualKeyCode.VK_C,
            "d" => VirtualKeyCode.VK_D, "e" => VirtualKeyCode.VK_E, "f" => VirtualKeyCode.VK_F,
            "g" => VirtualKeyCode.VK_G, "h" => VirtualKeyCode.VK_H, "i" => VirtualKeyCode.VK_I,
            "j" => VirtualKeyCode.VK_J, "k" => VirtualKeyCode.VK_K, "l" => VirtualKeyCode.VK_L,
            "m" => VirtualKeyCode.VK_M, "n" => VirtualKeyCode.VK_N, "o" => VirtualKeyCode.VK_O,
            "p" => VirtualKeyCode.VK_P, "q" => VirtualKeyCode.VK_Q, "r" => VirtualKeyCode.VK_R,
            "s" => VirtualKeyCode.VK_S, "t" => VirtualKeyCode.VK_T, "u" => VirtualKeyCode.VK_U,
            "v" => VirtualKeyCode.VK_V, "w" => VirtualKeyCode.VK_W, "x" => VirtualKeyCode.VK_X,
            "y" => VirtualKeyCode.VK_Y, "z" => VirtualKeyCode.VK_Z,
            _ => VirtualKeyCode.RETURN
        };

        // Core Audio ses seviyesi
        private static void SetMasterVolume(float level)
        {
            var objType = Type.GetTypeFromProgID("MMDeviceEnumerator");
            if (objType == null) throw new Exception("MMDeviceEnumerator bulunamadı.");
            // NAudio üzerinden yapalım
            using var device = new NAudio.CoreAudioApi.MMDeviceEnumerator()
                .GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);
        }

        // ── P/Invoke ─────────────────────────────────────────────────────────
        [DllImport("user32.dll")] private static extern bool LockWorkStation();
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
        [DllImport("PowrProf.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
    }
}
