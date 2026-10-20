using System.Collections.ObjectModel;
using ClipNest.App.Views;
using ClipNest.Core;
using ClipNest.Data;
using ClipNest.Platform;

namespace ClipNest.App.Services;

/// <summary>
/// Paste Stack: while open, every copy is queued; each Ctrl+V in another app pastes the next queued item.
/// The clipboard always holds the item that the next Ctrl+V will paste.
/// </summary>
public sealed class PasteStack : IDisposable
{
    private readonly AppHost _host;
    private KeyboardHook? _hook;
    private StackWindow? _window;
    private bool _newestFirst;

    public PasteStack(AppHost host)
    {
        _host = host;
        host.Ingested += OnIngested;
    }

    public ObservableCollection<ClipItem> Items { get; } = [];

    public bool IsOpen => _window is not null;

    /// <summary>False = first copied, first pasted (default). True = last copied, first pasted.</summary>
    public bool NewestFirst
    {
        get => _newestFirst;
        set
        {
            if (_newestFirst == value) return;
            _newestFirst = value;
            var reordered = Items.Reverse().ToList();
            Items.Clear();
            foreach (var i in reordered) Items.Add(i);
            PrimeClipboard();
        }
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (IsOpen) return;
        _host.Panel.HideNow();
        _hook = new KeyboardHook();
        _hook.PasteDetected += OnPasteDetected;
        _window = new StackWindow(_host, this);
        _window.Closed += (_, _) => Close();
        _window.ShowStack();
    }

    public void Close()
    {
        if (!IsOpen) return;
        _hook?.Dispose();
        _hook = null;
        var window = _window;
        _window = null;
        window?.Close();
        Items.Clear();
    }

    public void Remove(ClipItem item)
    {
        var wasHead = Items.Count > 0 && Items[0].Id == item.Id;
        Items.Remove(item);
        if (wasHead) PrimeClipboard();
    }

    public void Clear() => Items.Clear();

    private void OnIngested(IngestResult result)
    {
        if (!IsOpen) return;
        if (_newestFirst) Items.Insert(0, result.Item);
        else Items.Add(result.Item);
        PrimeClipboard();
    }

    /// <summary>Called on a pool thread right after the user's Ctrl+V reached the target app.</summary>
    private void OnPasteDetected()
    {
        // Give the target app time to read the clipboard before replacing its content.
        Thread.Sleep(120);
        _host.UI.TryEnqueue(() =>
        {
            if (!IsOpen || Items.Count == 0) return;
            Items.RemoveAt(0);
            PrimeClipboard();
        });
    }

    private void PrimeClipboard()
    {
        if (Items.Count == 0) return;
        var head = Items[0];
        _ = Task.Run(() =>
        {
            var payload = _host.BuildPayload([head]);
            _host.Writer.Write(payload, _host.Settings.AlwaysPlainText);
        });
    }

    public void Dispose()
    {
        _host.Ingested -= OnIngested;
        Close();
    }
}
