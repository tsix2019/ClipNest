namespace ClipNest.Core;

/// <summary>Cached presentation info for a source application.</summary>
/// <param name="Path">Full executable path (cache key).</param>
/// <param name="Name">Display name (file description or exe name).</param>
/// <param name="Color">Header color as ARGB, already contrast-adjusted.</param>
/// <param name="IconFile">Absolute path to a cached PNG icon, or null.</param>
public sealed record AppInfo(string Path, string Name, uint Color, string? IconFile);
