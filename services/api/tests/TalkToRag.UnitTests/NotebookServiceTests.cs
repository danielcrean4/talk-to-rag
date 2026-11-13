using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Notebooks;
using TalkToRag.Application.Sources;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;

namespace TalkToRag.UnitTests;

public class NotebookServiceTests
{
    private readonly InMemoryNotebookRepository _notebookRepo = new();
    private readonly InMemorySourceRepository _sourceRepo = new();
    private readonly TestCurrentUser _currentUser = new() { UserId = Guid.NewGuid() };
    private readonly FixedClock _clock = new();

    private NotebookService CreateService() =>
        new(_notebookRepo, _sourceRepo, _currentUser, _clock);

    [Fact]
    public async Task CreateAsync_CreatesNotebook_WhenValid()
    {
        var service = CreateService();
        var result = await service.CreateAsync(new CreateNotebookRequest("Deep Learning", "Notes on DL"), default);

        Assert.Equal("Deep Learning", result.Name);
        Assert.Equal("Notes on DL", result.Description);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(0, result.SourceCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_ThrowsRequestValidation_WhenNameEmpty(string name)
    {
        var service = CreateService();
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateAsync(new CreateNotebookRequest(name, null), default));
    }

    [Fact]
    public async Task CreateAsync_ThrowsConflict_WhenNameAlreadyExistsForUser()
    {
        var service = CreateService();
        await service.CreateAsync(new CreateNotebookRequest("RL", null), default);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new CreateNotebookRequest("RL", null), default));
    }

    [Fact]
    public async Task UpdateAsync_RenamesNotebook_WhenOnlyTheLetterCaseChanges()
    {
        var service = CreateService();
        var notebook = await service.CreateAsync(new CreateNotebookRequest("maths", null), default);

        var renamed = await service.UpdateAsync(notebook.Id, new UpdateNotebookRequest("Maths", null), default);

        Assert.Equal("Maths", renamed.Name);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyNotebooksForCaller()
    {
        var service = CreateService();
        await service.CreateAsync(new CreateNotebookRequest("My Notebook", null), default);

        // Another user's notebook
        _notebookRepo.Notebooks.Add(new Notebook
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Name = "Other User Notebook",
            CreatedAtUtc = _clock.GetUtcNow()
        });

        var list = await service.ListAsync(default);
        var single = Assert.Single(list);
        Assert.Equal("My Notebook", single.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsNotFound_WhenOwnedByOtherUser()
    {
        var service = CreateService();
        var id = Guid.NewGuid();
        _notebookRepo.Notebooks.Add(new Notebook
        {
            Id = id,
            UserId = Guid.NewGuid(),
            Name = "Other Notebook",
            CreatedAtUtc = _clock.GetUtcNow()
        });

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(id, default));
    }

    [Fact]
    public async Task AddSourceAsync_LinksSource_WhenOwnedByCaller()
    {
        var service = CreateService();
        var notebook = await service.CreateAsync(new CreateNotebookRequest("RL", null), default);

        var sourceId = Guid.NewGuid();
        _sourceRepo.All.Add(new Source
        {
            Id = sourceId,
            UserId = _currentUser.UserId,
            Title = "Sutton & Barto",
            FileName = "sutton.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/sutton.pdf"
        });

        await service.AddSourceAsync(notebook.Id, sourceId, default);

        var details = await service.GetByIdAsync(notebook.Id, default);
        Assert.Contains(sourceId, details.SourceIds);
    }

    [Fact]
    public async Task AddSourceAsync_ThrowsNotFound_WhenSourceBelongsToOtherUser()
    {
        var service = CreateService();
        var notebook = await service.CreateAsync(new CreateNotebookRequest("RL", null), default);

        var otherUserSourceId = Guid.NewGuid();
        _sourceRepo.All.Add(new Source
        {
            Id = otherUserSourceId,
            UserId = Guid.NewGuid(), // other user
            Title = "Private",
            FileName = "private.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/private.pdf"
        });

        await Assert.ThrowsAsync<NotFoundException>(() => service.AddSourceAsync(notebook.Id, otherUserSourceId, default));
    }

    private sealed class InMemoryNotebookRepository : INotebookRepository
    {
        public List<Notebook> Notebooks { get; } = [];
        public List<NotebookSource> Links { get; } = [];

        public Task<Notebook?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Notebooks.FirstOrDefault(n => n.Id == id && n.UserId == userId));

        public Task<IReadOnlyList<Notebook>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Notebook>>(Notebooks.Where(n => n.UserId == userId).ToList());

        public Task<bool> ExistsNameForUserAsync(string name, Guid userId, Guid? excludeId, CancellationToken cancellationToken) =>
            Task.FromResult(Notebooks.Any(n => n.UserId == userId && n.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && (!excludeId.HasValue || n.Id != excludeId.Value)));

        public Task AddAsync(Notebook notebook, CancellationToken cancellationToken)
        {
            Notebooks.Add(notebook);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Notebook notebook, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(Notebook notebook, CancellationToken cancellationToken)
        {
            Notebooks.Remove(notebook);
            Links.RemoveAll(l => l.NotebookId == notebook.Id);
            return Task.CompletedTask;
        }

        public Task<bool> IsSourceInNotebookAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken) =>
            Task.FromResult(Links.Any(l => l.NotebookId == notebookId && l.SourceId == sourceId));

        public Task AddSourceAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken)
        {
            Links.Add(new NotebookSource { NotebookId = notebookId, SourceId = sourceId });
            return Task.CompletedTask;
        }

        public Task RemoveSourceAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken)
        {
            Links.RemoveAll(l => l.NotebookId == notebookId && l.SourceId == sourceId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> GetSourceIdsForNotebookAsync(Guid notebookId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(Links.Where(l => l.NotebookId == notebookId).Select(l => l.SourceId).ToList());
    }

    private sealed class InMemorySourceRepository : ISourceRepository
    {
        public List<Source> All { get; } = [];

        public Task<SourceDto?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<SourceDto?>(null);

        public Task<Source?> FindEntityByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(All.FirstOrDefault(s => s.Id == id && s.UserId == userId));

        public Task<Source?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(All.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<SourceDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceDto>>([]);

        public Task<IReadOnlyList<Guid>> FindPendingOrProcessingIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<Guid>?> FindReadySourceIdsForScopeAsync(Guid userId, IReadOnlyList<Guid> notebookIds, IReadOnlyList<Guid> tagIds, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>?>(null);

        public Task SaveChunksAsync(Guid sourceId, IReadOnlyList<SourceChunk> chunks, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AddAsync(Source source, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateAsync(Source source, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> SetStatusAsync(Guid id, SourceStatus status, string? failureReason, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> SetDetailsForUserAsync(Guid id, Guid userId, string? license, string? sourceUrl, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> HasChunksAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> DeleteForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
