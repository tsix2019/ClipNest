namespace ClipNest.Core.Abstractions;

/// <summary>Which items to list. <see cref="PinboardId"/> null = full history (pinned items included).</summary>
public sealed record ClipQuery(SearchQuery Search, long? PinboardId = null, int Offset = 0, int Limit = 200);

/// <summary>Persistence for clips, pinboards and cached app info.</summary>
public interface IClipRepository
{
    ClipItem? Get(long id);
    ClipItem? FindByHash(string hash);

    /// <summary>Inserts and assigns <see cref="ClipItem.Id"/>; returns the id.</summary>
    long Insert(ClipItem item);

    /// <summary>Re-inserts a previously deleted item with its original id (undo).</summary>
    void Restore(ClipItem item);

    /// <summary>Marks an item as copied again: refreshes its time and source app so it moves to the front.</summary>
    void Touch(long id, DateTimeOffset at, string? appPath, string? appName);

    IReadOnlyList<ClipItem> Query(ClipQuery query);
    int Count(long? pinboardId = null);

    void Delete(long id);
    void Rename(long id, string? title);
    void UpdateText(long id, ClipKind kind, string text, string hash, int charCount);
    void SetPinboard(long id, long? pinboardId);
    void SetOcrText(long id, string text);
    void SetLinkTitle(long id, string title);

    /// <summary>Items still lacking OCR text (images only), newest first.</summary>
    IReadOnlyList<ClipItem> ImagesWithoutOcr(int limit);

    /// <summary>Deletes unpinned items created before <paramref name="cutoff"/>; returns their content hashes.</summary>
    IReadOnlyList<string> PurgeOlderThan(DateTimeOffset cutoff);

    /// <summary>Keeps only the newest <paramref name="max"/> unpinned items; returns removed hashes.</summary>
    IReadOnlyList<string> EnforceCap(int max);

    /// <summary>Deletes all unpinned items; returns removed hashes.</summary>
    IReadOnlyList<string> ClearHistory();

    IReadOnlyList<Pinboard> GetPinboards();
    long AddPinboard(string name, int colorIndex);
    void UpdatePinboard(Pinboard pinboard);

    /// <summary>Deletes a pinboard and every item in it; returns removed hashes.</summary>
    IReadOnlyList<string> DeletePinboard(long id);

    void ReorderPinboards(IReadOnlyList<long> orderedIds);

    AppInfo? GetApp(string path);
    void UpsertApp(AppInfo app);

    /// <summary>Distinct source app names, most recently used first (for search suggestions).</summary>
    IReadOnlyList<string> RecentAppNames(int limit);

    bool IsHashReferenced(string hash);
}
