namespace ErpApp.Domain.Crm;

/// <summary>
/// Aggregate root for the sales-pipeline CRM feature (architecture-spec.md §4.2 /
/// product-requirements.md FR-4.7), roadmap Phase 8+'s "CRM: Deals, SMS" bullet, Deals-only this
/// phase (SMS deferred to its own Phase 16 -- needs its own gateway/credit-ledger/template
/// infrastructure, the same reasoning that split the Reports module into 8a-8f). Confirmed live
/// shape: erp-module-scan.md's CRM section, "1. Deals" (line 85-87) -- a pipeline tracker with 3
/// status tabs (Pending/Won/Lost), list columns Closing Date/Created At/Details/Stage (inline
/// dropdown)/Contact/Expected Revenue/Assigned To (multi-avatar), and a New Deal form with no Stage
/// field at all -- Stage is set afterward via the list's own inline dropdown (MoveToStage), not at
/// creation, per architecture-spec.md §4.2's own named command shape (CreateDeal, UpdateStage,
/// MarkWon, MarkLost, AssignTo).
///
/// No Draft/Approve lifecycle at all, mirroring WorkTask (Phase 13) -- no Code, no
/// IDocumentNumberGenerator involvement, no DocumentNumberingRule row. Status is Pending/Won/Lost
/// instead of Draft/Approved.
///
/// Won and Lost are both terminal -- an explicit judgment call, not a silent default:
/// erp-module-scan.md's confirmed live UI never shows a "reopen"/"revert" action on a closed Deal,
/// only a Stage inline dropdown while still Pending, the same "no reopen action observed live"
/// reasoning that made WorkTask's Done terminal. Update/MoveToStage/AssignTo all reject once
/// Status != Pending (EnsureOpen), mirroring WorkTask's EnsureNotDone guard-method shape.
///
/// StageId is nullable and freely settable while Pending (no forward-only ordering) -- DealStage's
/// own SortOrder field is for display ordering on the config screen only, not an enforced
/// state-machine sequence, since the confirmed live UI shows a plain inline dropdown, not a
/// per-row forward-only checkmark the way WorkTaskStatus's "3 status tabs" is.
/// </summary>
public sealed class Deal
{
    private readonly List<DealAssignee> _assignees = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid? LeadSourceId { get; private set; }
    public decimal ExpectedRevenue { get; private set; }
    public DateOnly? ExpectedClosingDate { get; private set; }
    public Guid? StageId { get; private set; }
    public DealStatus Status { get; private set; }
    public bool IsPrivate { get; private set; }
    public DateOnly? ClosingDate { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<DealAssignee> Assignees => _assignees;

    private Deal()
    {
    }

    public static Deal Create(
        Guid organizationId,
        Guid contactId,
        string title,
        string? description,
        Guid? leadSourceId,
        decimal expectedRevenue,
        DateOnly? expectedClosingDate,
        bool isPrivate,
        Guid createdByUserId)
    {
        return new Deal
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Title = title,
            Description = description,
            LeadSourceId = leadSourceId,
            ExpectedRevenue = expectedRevenue,
            ExpectedClosingDate = expectedClosingDate,
            StageId = null,
            Status = DealStatus.Pending,
            IsPrivate = isPrivate,
            ClosingDate = null,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        string title,
        string? description,
        Guid? leadSourceId,
        decimal expectedRevenue,
        DateOnly? expectedClosingDate,
        bool isPrivate)
    {
        EnsureOpen();
        Title = title;
        Description = description;
        LeadSourceId = leadSourceId;
        ExpectedRevenue = expectedRevenue;
        ExpectedClosingDate = expectedClosingDate;
        IsPrivate = isPrivate;
    }

    /// <summary>Freely settable while Pending -- see this type's own doc comment on why StageId
    /// carries no forward-only ordering, unlike WorkTask.TransitionStatus.</summary>
    public void MoveToStage(Guid stageId)
    {
        EnsureOpen();
        StageId = stageId;
    }

    /// <summary>Terminal -- see this type's own doc comment. A further Update/MoveToStage/AssignTo
    /// call against a Won deal is rejected by EnsureOpen.</summary>
    public void MarkWon()
    {
        EnsureOpen();
        Status = DealStatus.Won;
        ClosingDate = DateOnly.FromDateTime(DateTime.UtcNow);
    }

    /// <summary>Terminal -- see this type's own doc comment.</summary>
    public void MarkLost()
    {
        EnsureOpen();
        Status = DealStatus.Lost;
        ClosingDate = DateOnly.FromDateTime(DateTime.UtcNow);
    }

    public void AddAssignee(Guid userId)
    {
        EnsureOpen();
        if (_assignees.Any(x => x.UserId == userId))
        {
            return;
        }

        _assignees.Add(DealAssignee.Create(Id, userId));
    }

    public void RemoveAssignee(Guid userId)
    {
        EnsureOpen();
        _assignees.RemoveAll(x => x.UserId == userId);
    }

    private void EnsureOpen()
    {
        if (Status != DealStatus.Pending)
        {
            throw new InvalidOperationException($"A {Status} deal can no longer be edited.");
        }
    }
}
