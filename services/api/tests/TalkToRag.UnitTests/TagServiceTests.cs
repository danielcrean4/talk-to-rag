using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Sources;
using TalkToRag.Application.Tags;
using TalkToRag.Domain.Entities;

namespace TalkToRag.UnitTests;

public class TagServiceTests
{
    private readonly InMemoryTagRepository _tagRepo = new();
    private readonly InMemorySourceRepository _sourceRepo = new();
    private readonly TestCurrentUser _currentUser = new() { UserId = Guid.NewGuid() };
    private readonly FixedClock _clock = new();

    private TagService CreateService() =>
        new(_tagRepo, _sourceRepo, _currentUser, _clock);

    [Fact]
    public async Task CreateAsync_CreatesTag_WhenValid()
    {
        var service = CreateService();
        var result = await service.CreateAsync(new CreateTagRequest("Machine Learning"), default);

        Assert.Equal("Machine Learning", result.Name);
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_ThrowsRequestValidation_WhenNameEmpty(string name)
    {
        var service = CreateService();
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateAsync(new CreateTagRequest(name), default));
    }

    [Fact]
    public async Task CreateAsync_ThrowsConflict_WhenNameAlreadyExistsForUser()
    {
        var service = CreateService();
        await service.CreateAsync(new CreateTagRequest("RL"), default);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new CreateTagRequest("RL"), default));
    }

    [Fact]
    public async Task AddTagToSourceAsync_LinksTag_WhenBothOwnedByCaller()
    {
        var service = CreateService();
        var tag = await service.CreateAsync(new CreateTagRequest("RL"), default);

        var sourceId = Guid.NewGuid();
        _sourceRepo.All.Add(new Source
        {
            Id = sourceId,
            UserId = _currentUser.UserId,
            Title = "TD Book",
            FileName = "td.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/td.pdf"
        });

        await service.AddTagToSourceAsync(sourceId, tag.Id, default);

        var tags = await service.GetTagIdsForSourceAsync(sourceId, default);
        Assert.Contains(tag.Id, tags);
    }

    [Fact]
    public async Task AddTagToSourceAsync_ThrowsNotFound_WhenTagOwnedByOtherUser()
    {
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        _sourceRepo.All.Add(new Source
        {
            Id = sourceId,
            UserId = _currentUser.UserId,
            Title = "TD Book",
            FileName = "td.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/td.pdf"
        });

        var otherUserTagId = Guid.NewGuid();
        _tagRepo.Tags.Add(new Tag
        {
            Id = otherUserTagId,
            UserId = Guid.NewGuid(), // other user
            Name = "Private Tag"
        });

        await Assert.ThrowsAsync<NotFoundException>(() => service.AddTagToSourceAsync(sourceId, otherUserTagId, default));
    }

    private sealed class InMemoryTagRepository : ITagRepository
    {
        public List<Tag> Tags { get; } = [];
        public List<SourceTag> Links { get; } = [];

        public Task<Tag?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Tags.FirstOrDefault(t => t.Id == id && t.UserId == userId));

        public Task<IReadOnlyList<Tag>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Tag>>(Tags.Where(t => t.UserId == userId).ToList());

        public Task<bool> ExistsNameForUserAsync(string name, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Tags.Any(t => t.UserId == userId && t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(Tag tag, CancellationToken cancellationToken)
        {
            Tags.Add(tag);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Tag tag, CancellationToken cancellationToken)
        {
            Tags.Remove(tag);
            Links.RemoveAll(l => l.TagId == tag.Id);
            return Task.CompletedTask;
        }

        public Task<bool> HasTagAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken) =>
            Task.FromResult(Links.Any(l => l.SourceId == sourceId && l.TagId == tagId));

        public Task AddTagToSourceAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken)
        {
            Links.Add(new SourceTag { SourceId = sourceId, TagId = tagId });
            return Task.CompletedTask;
        }

        public Task RemoveTagFromSourceAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken)
        {
            Links.RemoveAll(l => l.SourceId == sourceId && l.TagId == tagId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> GetTagIdsForSourceAsync(Guid sourceId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(Links.Where(l => l.SourceId == sourceId).Select(l => l.TagId).ToList());
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

        public Task SaveChunksAsync(Guid sourceId, IReadOnlyList<SourceChunk> chunks, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AddAsync(Source source, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateAsync(Source source, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
