namespace BookStitch.Controls;

public interface IConceptMinimalPresentationParticipant
{
    bool IsMinimalEditorOpen { get; }
    bool HasDraft { get; }
    void DiscardDraftAndClose();
}

public sealed class ConceptMinimalPresentationScope
{
    private IConceptMinimalPresentationParticipant? _activeParticipant;

    public IConceptMinimalPresentationParticipant? ActiveParticipant => _activeParticipant;

    public void Activate(IConceptMinimalPresentationParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        if (ReferenceEquals(_activeParticipant, participant))
            return;

        var previous = _activeParticipant;
        _activeParticipant = null;
        previous?.DiscardDraftAndClose();
        _activeParticipant = participant;
    }

    public void Release(IConceptMinimalPresentationParticipant participant)
    {
        if (ReferenceEquals(_activeParticipant, participant))
            _activeParticipant = null;
    }
}
