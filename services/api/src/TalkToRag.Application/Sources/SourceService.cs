using System.Security.Cryptography;
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
    public const long MaxSizeBytes = 200 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".md", ".txt"
    };

    public async Task<CreateSourceResponse> UploadAsync(string fileName, Stream stream, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new RequestValidationException("File name is required.");
        }

        var extension = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new RequestValidationException($"Unsupported file type '{extension}'. Allowed extensions are: .pdf, .md, .txt.");
        }

        if (stream.CanSeek && stream.Length == 0)
        {
            throw new RequestValidationException("File is empty.");
        }

        if (stream.CanSeek && stream.Length > MaxSizeBytes)
        {
            throw new RequestValidationException("File exceeds 200 MB limit.");
        }

        string? fileHash = null;
        if (stream.CanSeek)
        {
            stream.Position = 0;
            var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
            fileHash = Convert.ToHexStringLower(hashBytes);
            stream.Position = 0;
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

        var contentType = extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".md" => "text/markdown",
            _ => "text/plain"
        };

        var source = new Source
        {
            Id = sourceId,
            UserId = userId,
            Title = title,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            BlobKey = blobKey,
            FileHash = fileHash,
            Status = SourceStatus.Pending,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        await sourceRepository.AddAsync(source, cancellationToken);
        await queue.EnqueueAsync(sourceId, cancellationToken);

        return new CreateSourceResponse(source.Id, source.Title, source.Status.ToString());
    }

    public async Task<IReadOnlyList<SourceDto>> ListAsync(CancellationToken cancellationToken) =>
        await sourceRepository.ListForUserAsync(currentUser.UserId, cancellationToken);

    public async Task<SourceDto> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await sourceRepository.FindByIdForUserAsync(id, currentUser.UserId, cancellationToken)
            ?? throw new NotFoundException($"Source '{id}' not found.");

    public async Task<SourceFileResponse> GetFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var source = await RequireSourceAsync(id, cancellationToken);
        var stream = await blobStorage.OpenReadAsync(source.BlobKey, cancellationToken);
        return new SourceFileResponse(stream, source.ContentType, source.FileName);
    }

    public async Task RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var source = await RequireSourceAsync(id, cancellationToken);
        if (source.Status is SourceStatus.Pending or SourceStatus.Processing)
        {
            throw new ConflictException($"Source '{id}' is already waiting to be processed.");
        }

        source.Status = SourceStatus.Pending;
        source.FailureReason = null;
        await sourceRepository.UpdateAsync(source, cancellationToken);
        await queue.EnqueueAsync(source.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var source = await sourceRepository.FindEntityByIdForUserAsync(id, userId, cancellationToken);
        if (source is null || !await sourceRepository.DeleteForUserAsync(id, userId, cancellationToken))
        {
            throw new NotFoundException($"Source '{id}' not found.");
        }

        await blobStorage.DeleteAsync(source.BlobKey, cancellationToken);
    }

    private async Task<Source> RequireSourceAsync(Guid id, CancellationToken cancellationToken) =>
        await sourceRepository.FindEntityByIdForUserAsync(id, currentUser.UserId, cancellationToken)
            ?? throw new NotFoundException($"Source '{id}' not found.");
}
