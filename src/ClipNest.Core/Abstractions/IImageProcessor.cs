namespace ClipNest.Core.Abstractions;

/// <summary>Image work that needs OS codecs; implemented by the platform layer.</summary>
public interface IImageProcessor
{
    /// <summary>Returns a PNG whose longest side is at most <paramref name="maxSide"/> pixels.</summary>
    byte[] CreateThumbnailPng(byte[] png, int maxSide);
}
