using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;

namespace TalkToRag.Infrastructure.Storage;

public class LocalBlobStorage(string rootPath) : IBlobStorage
{
    private readonly string _root = Path.GetFullPath(rootPath);

    public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        var targetPath = ResolvePath(key);
        var directory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".tmp_{Guid.NewGuid():N}");
        try
        {
            await using (var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await content.CopyToAsync(fileStream, cancellationToken);
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best effort cleanup */ }
            }
        }
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var targetPath = ResolvePath(key);
        if (!File.Exists(targetPath))
        {
            throw new NotFoundException($"Blob '{key}' not found.");
        }

        Stream stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var targetPath = ResolvePath(key);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var targetPath = ResolvePath(key);
        return Task.FromResult(File.Exists(targetPath));
    }

    private string ResolvePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains('\\') || key.StartsWith('/') || key.Split('/').Any(p => p == ".."))
        {
            throw new RequestValidationException($"Invalid blob key '{key}'.");
        }

        var normalizedKey = key.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_root, normalizedKey));

        // Prevent path traversal outside root
        if (!fullPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
        {
            throw new RequestValidationException($"Invalid blob key '{key}'.");
        }

        return fullPath;
    }
}
