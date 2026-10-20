using ClipNest.Platform.Native;

namespace ClipNest.Platform;

/// <summary>
/// Raises <see cref="Changed"/> (on the UI thread) shortly after the clipboard content changes.
/// Bursts of updates — apps often set several formats in a row — are coalesced.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Timer _debounce;
    private readonly SynchronizationContext? _context;
    private uint _lastSequence;

    public event Action? Changed;

    /// <summary>When true, updates are ignored (user paused recording).</summary>
    public bool Paused { get; set; }

    public ClipboardMonitor(MessageWindow window)
    {
        _window = window;
        _context = SynchronizationContext.Current;
        _debounce = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        _window.AddHandler(OnMessage);
        if (!User32.AddClipboardFormatListener(window.Handle))
            throw new InvalidOperationException("AddClipboardFormatListener failed");
    }

    private bool OnMessage(uint msg, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (msg != User32.WM_CLIPBOARDUPDATE) return false;
        if (!Paused) _debounce.Change(60, Timeout.Infinite);
        return true;
    }

    private void Fire()
    {
        var seq = User32.GetClipboardSequenceNumber();
        if (seq == _lastSequence) return;
        _lastSequence = seq;
        if (_context is not null) _context.Post(_ => Changed?.Invoke(), null);
        else Changed?.Invoke();
    }

    public void Dispose()
    {
        User32.RemoveClipboardFormatListener(_window.Handle);
        _window.RemoveHandler(OnMessage);
        _debounce.Dispose();
    }
}
