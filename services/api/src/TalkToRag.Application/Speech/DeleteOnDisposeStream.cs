using TalkToRag.Application.Common.Interfaces;

namespace TalkToRag.Application.Speech;

/// <summary>Reads a blob and deletes it when disposed, so a file handed to the caller is gone once it has been sent.</summary>
internal sealed class DeleteOnDisposeStream(Stream inner, IBlobStorage blobStorage, string blobKey) : Stream
{
    private bool _disposed;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            inner.Dispose();
            blobStorage.DeleteAsync(blobKey, CancellationToken.None).GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await inner.DisposeAsync();
            await blobStorage.DeleteAsync(blobKey, CancellationToken.None);
        }

        GC.SuppressFinalize(this);
    }
}
