namespace TalkToRag.Application.Common.Interfaces;

/// <summary>
/// Stores and retrieves binary assets (uploaded sources, audio) by key ("sources/{id}.pdf").
/// The AI service opens the same directory, so the services pass each other keys, never bytes.
/// </summary>
public interface IBlobStorage
{
    Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
