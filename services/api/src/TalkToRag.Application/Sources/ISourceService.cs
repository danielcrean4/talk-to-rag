namespace TalkToRag.Application.Sources;

public interface ISourceService
{
    Task<CreateSourceResponse> UploadAsync(string fileName, Stream stream, CancellationToken cancellationToken);
    Task<IReadOnlyList<SourceDto>> ListAsync(CancellationToken cancellationToken);
    Task<SourceDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<SourceFileResponse> GetFileAsync(Guid id, CancellationToken cancellationToken);
    Task RetryAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
