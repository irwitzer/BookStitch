namespace BookStitch.Services;

public sealed class ChapterSourcePolicy
{
    private static readonly IReadOnlyDictionary<ChapterCandidateSource, int> DefaultPriorities =
        new Dictionary<ChapterCandidateSource, int>
        {
            [ChapterCandidateSource.EmbeddedChapterTitle] = 1,
            [ChapterCandidateSource.TrackTitle] = 2,
            [ChapterCandidateSource.Filename] = 3,
            [ChapterCandidateSource.CanonicalTitle] = 4,
            [ChapterCandidateSource.CanonicalAlbum] = 5
        };

    private readonly IReadOnlyDictionary<ChapterCandidateSource, int> _priorities;

    public ChapterSourcePolicy()
        : this(DefaultPriorities)
    {
    }

    public ChapterSourcePolicy(IReadOnlyDictionary<ChapterCandidateSource, int> priorities)
    {
        ArgumentNullException.ThrowIfNull(priorities);

        var copy = new Dictionary<ChapterCandidateSource, int>();
        foreach (var source in DefaultPriorities.Keys)
        {
            if (!priorities.TryGetValue(source, out var priority) || priority < 0)
            {
                throw new ArgumentException(
                    $"A non-negative resolve priority is required for '{source}'.",
                    nameof(priorities));
            }

            copy.Add(source, priority);
        }

        if (priorities.Keys.Any(source => !DefaultPriorities.ContainsKey(source)))
            throw new ArgumentException("The source policy contains a non-resolvable source.", nameof(priorities));

        _priorities = copy;
    }

    public bool IsSelectable(ChapterCandidateSource source) => _priorities.ContainsKey(source);

    public int GetTieBreakPriority(ChapterCandidateSource source)
    {
        if (_priorities.TryGetValue(source, out var priority))
            return priority;

        throw new ArgumentOutOfRangeException(nameof(source), source, "The source cannot participate in chapter resolve.");
    }
}
