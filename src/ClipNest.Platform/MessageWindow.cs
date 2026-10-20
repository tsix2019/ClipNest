using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ClipNest.Platform.Native;
using static ClipNest.Platform.Native.User32;

namespace ClipNest.Platform;

/// <summary>Return true from a handler to mark the message handled and supply <paramref name="result"/>.</summary>
public delegate bool MessageHandler(uint msg, nint wParam, nint lParam, out nint result);

/// <summary>
/// A hidden top-level window that receives clipboard updates, hotkeys, tray callbacks and broadcasts
/// (such as "TaskbarCreated"). It must be created on a thread that pumps messages (the UI thread).
/// </summary>
public sealed unsafe class MessageWindow : IDisposable
{
    public const string ClassName = "ClipNest.MessageWindow";

    private static readonly Dictionary<nint, MessageWindow> Windows = [];
    private static bool _classRegistered;
    private readonly List<MessageHandler> _handlers = [];

    public nint Handle { get; private set; }

    public MessageWindow()
    {
        var hInstance = Kernel32.GetModuleHandleW(null);
        if (!_classRegistered)
        {
            fixed (char* name = ClassName)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &WndProc,
                    hInstance = hInstance,
                    lpszClassName = name,
                };
                if (RegisterClassExW(in wc) == 0)
                    throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");
            }
            _classRegistered = true;
        }

        Handle = CreateWindowExW(WS_EX_TOOLWINDOW, ClassName, "ClipNest", WS_POPUP, 0, 0, 0, 0, 0, 0, hInstance, 0);
        if (Handle == 0)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        Windows[Handle] = this;
    }

    /// <summary>Handlers run in registration order until one reports the message handled.</summary>
    public void AddHandler(MessageHandler handler) => _handlers.Add(handler);

    public void RemoveHandler(MessageHandler handler) => _handlers.Remove(handler);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (Windows.TryGetValue(hwnd, out var window))
        {
            try
            {
                foreach (var handler in window._handlers.ToArray())
                {
                    if (handler(msg, wParam, lParam, out var result)) return result;
                }
            }
            catch (Exception e)
            {
                // Exceptions must never cross the native boundary.
                Log.Error("WndProc", e);
            }
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (Handle == 0) return;
        Windows.Remove(Handle);
        DestroyWindow(Handle);
        Handle = 0;
    }
}
