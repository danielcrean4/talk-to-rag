using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;

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

            await ProcessSourceAsync(sourceId, stoppingToken);
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
        if (source is null)
        {
            logger.LogWarning("Ingestion job skipped: Source {SourceId} not found", sourceId);
            return;
        }

        source.Status = SourceStatus.Processing;
        source.FailureReason = null;
        await repo.UpdateAsync(source, cancellationToken);

        try
        {
            var request = new IngestSourceRequest(source.Id, source.BlobKey, source.ContentType);
            var response = await aiClient.IngestSourceAsync(request, cancellationToken);

            source.Chunks.Clear();
            foreach (var chunk in response.Chunks)
            {
                source.Chunks.Add(new SourceChunk
                {
                    Id = Guid.NewGuid(),
                    SourceId = source.Id,
                    PageNumber = chunk.PageNumber,
                    Section = chunk.Section,
                    ChunkIndex = chunk.ChunkIndex,
                    Content = chunk.Content,
                    Embedding = chunk.Embedding
                });
            }

            source.Status = SourceStatus.Ready;
            source.FailureReason = null;
            await repo.UpdateAsync(source, cancellationToken);
            logger.LogInformation("Successfully ingested source {SourceId} into {ChunkCount} chunks", source.Id, source.Chunks.Count);
        }
        catch (AiIngestionException ex)
        {
            logger.LogWarning(ex, "Source {SourceId} rejected by AI service: {Reason}", source.Id, ex.Message);
            source.Status = SourceStatus.Failed;
            source.FailureReason = ex.Message;
            await repo.UpdateAsync(source, CancellationToken.None);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to process source {SourceId}", source.Id);
            source.Status = SourceStatus.Failed;
            source.FailureReason = ex.Message;
            await repo.UpdateAsync(source, CancellationToken.None);
        }
    }
}
