namespace TalkToRag.Application.Common.Interfaces;

/// <summary>
/// Stores and retrieves binary assets (uploaded sources, audio) by key ("sources/{id}.pdf").
/// Backed by a local directory in development and Docker, swappable for cloud storage later.
/// </summary>
public interface IBlobStorage
{
    Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
