using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;
using TalkToRag.Infrastructure.Audit;

namespace TalkToRag.Infrastructure.Ingestion;

public sealed class SourceIngestionWorker(
    IServiceScopeFactory scopeFactory,
    ISourceIngestionQueue queue,
    ILogger<SourceIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReEnqueueUnfinishedJobsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid sourceId;
            try
            {
                sourceId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await ProcessSourceAsync(sourceId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error processing source {SourceId}; worker continuing", sourceId);
            }
        }
    }

    private async Task ReEnqueueUnfinishedJobsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISourceRepository>();
            var pendingIds = await repo.FindPendingOrProcessingIdsAsync(cancellationToken);

            foreach (var id in pendingIds)
            {
                logger.LogInformation("Re-enqueuing unfinished source ingestion {SourceId}", id);
                await queue.EnqueueAsync(id, cancellationToken);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to recover pending ingestion jobs on startup");
        }
    }

    private async Task ProcessSourceAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISourceRepository>();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

        var source = await repo.FindByIdAsync(sourceId, cancellationToken);
        if (source is null || !await repo.SetStatusAsync(sourceId, SourceStatus.Processing, null, cancellationToken))
        {
            logger.LogInformation("Ingestion job skipped: source {SourceId} no longer exists", sourceId);
            return;
        }

        try
        {
            var request = new IngestSourceRequest(source.Id, source.BlobKey, source.ContentType);
            IngestSourceResponse response;
            using (BackgroundAuditUser.Begin(source.UserId))
            {
                response = await aiClient.IngestSourceAsync(request, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(response.EmbeddingModel))
            {
                if (await MarkFailedAsync(repo, source.Id, "The AI service did not say which embedding model it used. Update the AI service and retry."))
                {
                    logger.LogWarning("Source {SourceId} failed: the ingest response named no embedding model", source.Id);
                }

                return;
            }

            var chunks = response.Chunks.Select(chunk => new SourceChunk
            {
                Id = Guid.CreateVersion7(),
                SourceId = source.Id,
                PageNumber = chunk.PageNumber,
                PageLabel = chunk.PageLabel,
                Section = chunk.Section,
                ChunkIndex = chunk.ChunkIndex,
                Content = chunk.Content,
                Embedding = chunk.Embedding,
                EmbeddingModel = response.EmbeddingModel
            }).ToList();

            await repo.SaveChunksAsync(source.Id, chunks, cancellationToken);

            if (await repo.SetStatusAsync(source.Id, SourceStatus.Ready, null, cancellationToken))
            {
                logger.LogInformation("Successfully ingested source {SourceId} into {ChunkCount} chunks", source.Id, chunks.Count);
            }
            else
            {
                logger.LogInformation("Source {SourceId} was deleted while it was being ingested", source.Id);
            }
        }
        catch (AiIngestionException ex)
        {
            if (await MarkFailedAsync(repo, source.Id, ex.Message))
            {
                logger.LogWarning(ex, "Source {SourceId} rejected by AI service: {Reason}", source.Id, ex.Message);
            }
        }
        catch (AiServiceUnavailableException ex)
        {
            if (await MarkFailedAsync(repo, source.Id, $"Embedding service unavailable: {ex.Message}"))
            {
                logger.LogWarning(ex, "Source {SourceId} failed: AI service unavailable ({Reason})", source.Id, ex.Message);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (await MarkFailedAsync(repo, source.Id, "Ingestion timed out while communicating with AI service."))
            {
                logger.LogError(ex, "AI service timed out while ingesting source {SourceId}", source.Id);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (await MarkFailedAsync(repo, source.Id, "Ingestion failed unexpectedly. Please retry."))
            {
                logger.LogError(ex, "Failed to process source {SourceId}", source.Id);
            }
        }
    }

    /// <summary>
    /// A source that still has chunks from an earlier ingestion stays Ready and searchable with them;
    /// the reason is kept so the failed attempt is visible.
    /// </summary>
    /// <returns>False when the source was deleted while it was being ingested.</returns>
    private async Task<bool> MarkFailedAsync(ISourceRepository repo, Guid sourceId, string reason)
    {
        try
        {
            var keepsOldChunks = await repo.HasChunksAsync(sourceId, CancellationToken.None);
            var status = keepsOldChunks ? SourceStatus.Ready : SourceStatus.Failed;
            var failureReason = keepsOldChunks ? $"Processing again failed, so the earlier passages are kept: {reason}" : reason;
            if (await repo.SetStatusAsync(sourceId, status, failureReason, CancellationToken.None))
            {
                return true;
            }

            logger.LogInformation("Source {SourceId} was deleted while it was being ingested", sourceId);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist status update for source {SourceId}", sourceId);
            return true;
        }
    }
}
