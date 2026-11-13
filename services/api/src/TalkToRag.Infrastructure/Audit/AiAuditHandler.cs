using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;
using TalkToRag.Infrastructure.Persistence;

namespace TalkToRag.Infrastructure.Audit;

/// <summary>
/// Sits on the HTTP client that talks to the AI service and stores every exchange as an <see cref="AiCall"/>.
/// Being at the transport, it covers every call without each caller having to remember to log.
/// </summary>
public sealed class AiAuditHandler(
    IServiceScopeFactory scopeFactory,
    IAuditContext auditContext,
    TimeProvider timeProvider,
    ILogger<AiAuditHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var call = new AiCall
        {
            Id = Guid.CreateVersion7(),
            UserId = auditContext.UserId,
            RequestTraceId = auditContext.RequestTraceId,
            Operation = request.RequestUri?.AbsolutePath ?? "unknown",
            Request = "{}",
            RequestRedacted = "{}",
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception error)
        {
            call.Error = error.Message;
            await SaveAsync(call, requestBody, responseBody: null, isEventStream: false, stopwatch);
            throw;
        }

        call.StatusCode = (int)response.StatusCode;
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            // A stream is only complete when the caller has finished reading it, so it is recorded then.
            var inner = await response.Content.ReadAsStreamAsync(cancellationToken);
            var capturing = new CapturingStream(inner, body => SaveAsync(call, requestBody, body, isEventStream: true, stopwatch));
            var content = new StreamContent(capturing);
            foreach (var header in response.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content = content;
            return response;
        }

        await response.Content.LoadIntoBufferAsync(cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        await SaveAsync(call, requestBody, responseBody, isEventStream: false, stopwatch);
        return response;
    }

    /// <summary>Never throws: losing an audit row must not fail the user's request.</summary>
    private async Task SaveAsync(AiCall call, string? requestBody, string? responseBody, bool isEventStream, Stopwatch stopwatch)
    {
        try
        {
            call.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            var request = AuditJson.ToNode(requestBody);
            var response = responseBody is null ? null : AuditJson.ToNode(responseBody, isEventStream);
            var (redactedRequest, redactedResponse) = AuditJson.Redact(request, response);
            call.Request = AuditJson.Serialize(request);
            call.RequestRedacted = AuditJson.Serialize(redactedRequest);
            call.Response = response is null ? null : AuditJson.Serialize(response);
            call.ResponseRedacted = response is null ? null : AuditJson.Serialize(redactedResponse);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TalkToRagDbContext>();
            db.AiCalls.Add(call);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Could not store the audit record for {Operation}", call.Operation);
        }
    }

    /// <summary>Passes reads through while keeping a copy, and hands the copy over when the reader is done.</summary>
    private sealed class CapturingStream(Stream inner, Func<string, Task> onCompleted) : Stream
    {
        private readonly MemoryStream _copy = new();
        private bool _completed;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            _copy.Write(buffer, offset, read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            _copy.Write(buffer.Span[..read]);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_completed)
            {
                _completed = true;
                inner.Dispose();
                _ = onCompleted(Encoding.UTF8.GetString(_copy.ToArray()));
            }

            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
