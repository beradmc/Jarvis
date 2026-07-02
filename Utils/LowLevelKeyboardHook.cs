using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Timers;

namespace JarvisCSharp.Utils;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) that intercepts keys system-wide
/// BEFORE any other application sees them — including fullscreen games,
/// the lock screen, and UAC prompts.
///
/// This is the same mechanism used by:
///   - Windows built-in shortcuts (Win+L, Win+D, etc.)
///   - Discord push-to-talk
///   - OBS Studio hotkeys
///   - Steam overlay
///
/// Unlike RegisterHotKey (which fails when another app has registered the
/// same combo or when a fullscreen game is eating input), WH_KEYBOARD_LL
/// sits at the very top of the input chain and always receives events.
///
/// Win+J handling:
///   When Win is pressed we start a short timer. If J arrives within 250ms,
///   we treat it as the Jarvis hotkey: fire SummonPressed, swallow both
///   keys, and eat the Win release so the Start menu doesn't stay open.
///   If J doesn't arrive, the timer expires and the Win key is passed
///   through normally (e.g., for Win+D, Win+E, etc.).
/// </summary>
public sealed class LowLevelKeyboardHook : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    // Virtual key codes
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_J = 0x4A;
    private const int VK_ESCAPE = 0x1B;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private bool _disposed;

    // Track modifier state
    private bool _winDown;
    private bool _comboFired;
    private System.Timers.Timer? _winTimer;

    /// <summary>Fired when Win+J is pressed (summon Jarvis overlay).</summary>
    public event Action? SummonPressed;

    /// <summary>Fired when Escape is pressed (dismiss overlay).</summary>
    public event Action? EscapePressed;

    /// <summary>
    /// Install the hook. Call once at startup. The hook remains active
    /// until Dispose() is called.
    /// </summary>
    public void Install()
    {
        if (_hookId != IntPtr.Zero || _disposed) return;

        _proc = HookCallback;

        // In single-file self-contained apps, Process.MainModule can throw.
        // GetModuleHandle(null) returns the handle of the current executable,
        // which works fine for WH_KEYBOARD_LL.
        IntPtr hMod;
        try
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            hMod = GetModuleHandle(curModule?.ModuleName ?? null!);
        }
        catch
        {
            hMod = GetModuleHandle(null!);
        }

        if (hMod == IntPtr.Zero) hMod = GetModuleHandle(null!);

        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);

        if (_hookId == IntPtr.Zero)
        {
            Debug.WriteLine($"[KeyboardHook] Failed to install hook (err={Marshal.GetLastWin32Error()})");
        }
        else
        {
            Debug.WriteLine("[KeyboardHook] WH_KEYBOARD_LL installed");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var wp = wParam.ToInt64();
            int vk = Marshal.ReadInt32(lParam);
            bool isKeyDown = wp == WM_KEYDOWN || wp == WM_SYSKEYDOWN;
            bool isKeyUp = wp == WM_KEYUP || wp == WM_SYSKEYUP;

            if (isKeyDown)
            {
                if (vk == VK_LWIN || vk == VK_RWIN)
                {
                    _winDown = true;
                    _comboFired = false;
                    // Start timer: if no J arrives within 250ms, treat Win as normal
                    StartWinTimer();
                }
                else if (_winDown && vk == VK_J)
                {
                    // Win+J combo detected
                    CancelWinTimer();
                    _comboFired = true;
                    _winDown = false;
                    SummonPressed?.Invoke();
                    // Swallow the J key so nothing else processes it
                    return new IntPtr(1);
                }
                else if (_winDown)
                {
                    // Some other key pressed while Win is held (Win+D, Win+E, etc.)
                    // Cancel our timer and let Windows handle it normally
                    CancelWinTimer();
                    _winDown = false;
                }
                else if (vk == VK_ESCAPE)
                {
                    EscapePressed?.Invoke();
                }
            }

            if (isKeyUp)
            {
                if (vk == VK_LWIN || vk == VK_RWIN)
                {
                    _winDown = false;
                    if (_comboFired)
                    {
                        _comboFired = false;
                        // Swallow the Win release so the Start menu closes
                        return new IntPtr(1);
                    }
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void StartWinTimer()
    {
        CancelWinTimer();
        _winTimer = new System.Timers.Timer(250) { AutoReset = false };
        _winTimer.Elapsed += (_, _) =>
        {
            // Timer expired: Win was pressed but no J followed.
            // Let the Win key pass through normally.
            _winDown = false;
        };
        _winTimer.Start();
    }

    private void CancelWinTimer()
    {
        if (_winTimer != null)
        {
            _winTimer.Stop();
            _winTimer.Dispose();
            _winTimer = null;
        }
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        CancelWinTimer();
        _disposed = true;
    }
}
