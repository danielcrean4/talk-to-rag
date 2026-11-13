using TalkToRag.Domain.Enums;

namespace TalkToRag.Domain.Entities;

/// <summary>An uploaded file. The bytes live in blob storage under <see cref="BlobKey"/>; this row is its record.</summary>
public class Source
{
    public const int MaxLicenseLength = 200;
    public const int MaxSourceUrlLength = 2000;

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Title { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string BlobKey { get; set; }
    public SourceStatus Status { get; set; } = SourceStatus.Pending;
    public string? FailureReason { get; set; }
    public string? FileHash { get; set; }

    /// <summary>The license the user recorded for the source, as free text such as "CC BY 4.0".</summary>
    public string? License { get; set; }

    /// <summary>Where the source came from: an absolute http or https URL.</summary>
    public string? SourceUrl { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<SourceChunk> Chunks { get; } = [];
    public List<NotebookSource> Notebooks { get; } = [];
    public List<SourceTag> Tags { get; } = [];
}
