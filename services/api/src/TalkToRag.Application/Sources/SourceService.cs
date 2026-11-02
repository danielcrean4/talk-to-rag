using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;

namespace TalkToRag.Application.Sources;

public sealed class SourceService(
    ISourceRepository sourceRepository,
    IBlobStorage blobStorage,
    ISourceIngestionQueue queue,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ISourceService
{
    private const long MaxSizeBytes = 50 * 1024 * 1024; // 50 MB
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".md", ".txt"
    };

    public async Task<CreateSourceResponse> UploadAsync(string fileName, string contentType, Stream stream, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new RequestValidationException("File name is required.");
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new RequestValidationException($"Unsupported file type '{extension}'. Allowed extensions are: .pdf, .md, .txt.");
        }

        if (stream.CanSeek && stream.Length == 0)
        {
            throw new RequestValidationException("File is empty.");
        }

        if (stream.CanSeek && stream.Length > MaxSizeBytes)
        {
            throw new RequestValidationException("File exceeds 50 MB limit.");
        }

        var sourceId = Guid.CreateVersion7();
        var blobKey = $"sources/{sourceId:N}{extension.ToLowerInvariant()}";

        await blobStorage.WriteAsync(blobKey, stream, cancellationToken);

        var sizeBytes = stream.CanSeek ? stream.Length : 0;
        var title = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (string.IsNullOrEmpty(title))
        {
            title = "Untitled Source";
        }

        var normalizedContentType = extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".md" => "text/markdown",
            ".txt" => "text/plain",
            _ => contentType
        };

        var source = new Source
        {
            Id = sourceId,
            UserId = userId,
            Title = title,
            FileName = fileName,
            ContentType = normalizedContentType,
            SizeBytes = sizeBytes,
            BlobKey = blobKey,
            Status = SourceStatus.Pending,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        await sourceRepository.AddAsync(source, cancellationToken);
        await queue.EnqueueAsync(sourceId, cancellationToken);

        return new CreateSourceResponse(source.Id, source.Title, source.Status.ToString());
    }

    public async Task<IReadOnlyList<SourceDto>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        return await sourceRepository.ListForUserAsync(userId, cancellationToken);
    }

    public async Task<SourceDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var source = await sourceRepository.FindByIdForUserAsync(id, userId, cancellationToken);
        if (source is null)
        {
            throw new NotFoundException($"Source '{id}' not found.");
        }

        return source;
    }
}
