using System.Threading.Channels;
using ClipNest.Core;
using ClipNest.Platform;

namespace ClipNest.App.Services;

/// <summary>
/// Background, one-at-a-time post-processing of new items: OCR for images and title lookup for links.
/// Results are written to the repository and announced through <see cref="AppHost.NotifyItemChanged"/>.
/// </summary>
public sealed class Enricher : IDisposable
{
    private readonly AppHost _host;
    private readonly Channel<ClipItem> _queue = Channel.CreateBounded<ClipItem>(new BoundedChannelOptions(200)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });
    private readonly CancellationTokenSource _stop = new();
    private readonly Lazy<OcrService> _ocr = new(() => new OcrService());
    private readonly Lazy<LinkTitleFetcher> _links = new(() => new LinkTitleFetcher());

    public Enricher(AppHost host)
    {
        _host = host;
        _ = Task.Run(RunAsync);
    }

    public void Enqueue(ClipItem item)
    {
        if (item.Kind == ClipKind.Image && _host.Settings.OcrImages && item.OcrText is null
            || item.Kind == ClipKind.Link && _host.Settings.FetchLinkTitles && item.LinkTitle is null)
        {
            _queue.Writer.TryWrite(item);
        }
    }

    /// <summary>Queues older images that were never OCR'd (e.g. recorded while OCR was off).</summary>
    public void Backfill()
    {
        if (!_host.Settings.OcrImages) return;
        foreach (var item in _host.Repository.ImagesWithoutOcr(100)) _queue.Writer.TryWrite(item);
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(_stop.Token))
            {
                try
                {
                    await ProcessAsync(item);
                }
                catch (Exception e)
                {
                    Log.Error($"Enrich item {item.Id}", e);
                }
                await Task.Delay(50, _stop.Token); // stay gentle on the CPU
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessAsync(ClipItem item)
    {
        if (_host.Repository.Get(item.Id) is not { } current) return; // deleted meanwhile

        if (current.Kind == ClipKind.Image && current.OcrText is null && _host.Settings.OcrImages)
        {
            var png = _host.Blobs.Read(current.ContentHash, BlobFlags.Png);
            if (png is null || !_ocr.Value.IsAvailable) return;
            var text = await _ocr.Value.RecognizeAsync(png);
            _host.Repository.SetOcrText(current.Id, text); // "" marks "done, nothing found"
            // Re-read: the user may have renamed or reused the item while OCR ran.
            if (_host.Repository.Get(current.Id) is { } fresh) _host.NotifyItemChanged(fresh);
        }
        else if (current.Kind == ClipKind.Link && current.LinkTitle is null && _host.Settings.FetchLinkTitles && current.Text is { } url)
        {
            var title = await _links.Value.FetchAsync(url, _stop.Token);
            _host.Repository.SetLinkTitle(current.Id, title ?? "");
            if (title is not null && _host.Repository.Get(current.Id) is { } fresh) _host.NotifyItemChanged(fresh);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_links.IsValueCreated) _links.Value.Dispose();
    }
}
