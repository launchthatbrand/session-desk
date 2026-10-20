using Microsoft.EntityFrameworkCore;
using SessionDesk.Api.Auth;
using SessionDesk.Api.Data;

namespace SessionDesk.Api.Briefing;

public enum BriefingKind
{
    Ready,
    Withheld,
    NotFound,
    Unavailable,
    Conflict,
    Empty,
}

public sealed record BriefingOutcome(
    BriefingKind Kind,
    Guid? BriefingId = null,
    string? Draft = null,
    string? Model = null,
    string? PromptVersion = null,
    string? Error = null);

public sealed class BriefingService(AppDbContext db, CommitteeContext scope, IConfiguration configuration, IMiniMaxClient miniMax)
{
    public const string PromptVersion = "briefing-v1";

    private const string SystemPrompt = """
        You write a short staff brief for a Florida Senate committee desk.
        Cite only the bill numbers listed in the packet.
        Do not recommend reporting, postponing, or placing a bill on the agenda.
        Write plain sentences. No headings.
        """;

    public async Task<BriefingOutcome> RequestAsync(Guid billId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["MiniMax:ApiKey"]))
        {
            return new BriefingOutcome(BriefingKind.Unavailable, Error: "Briefing is not configured.");
        }

        using var retrieve = BriefingTelemetry.Source.StartActivity("retrieve");
        var bill = await db.Bills.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == billId && item.CommitteeId == scope.CommitteeId, cancellationToken);
        if (bill is null)
        {
            return new BriefingOutcome(BriefingKind.NotFound);
        }

        var committeeBills = await db.Bills.AsNoTracking()
            .Where(item => item.CommitteeId == scope.CommitteeId)
            .Select(item => new { item.Id, item.Number, item.Title })
            .ToListAsync(cancellationToken);
        var actions = await db.BillActions.AsNoTracking()
            .Where(item => item.BillId == bill.Id)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new { item.Action, item.Note, item.CreatedAt, item.HearingId })
            .ToListAsync(cancellationToken);
        var hearingIds = actions.Where(item => item.HearingId != null).Select(item => item.HearingId!.Value).ToList();
        var hearings = await db.Hearings.AsNoTracking()
            .Where(item => item.CommitteeId == scope.CommitteeId && hearingIds.Contains(item.Id))
            .Select(item => new { item.StartsAt, item.Room })
            .ToListAsync(cancellationToken);

        var allowed = committeeBills
            .Select(item => CitationCheck.Normalize(item.Number))
            .ToHashSet(StringComparer.Ordinal);
        retrieve?.SetTag("bill.number", bill.Number);
        retrieve?.SetTag("retrieved.count", allowed.Count);

        using var prompt = BriefingTelemetry.Source.StartActivity("prompt");
        prompt?.SetTag("prompt.version", PromptVersion);
        var packet = BuildPacket(bill, committeeBills.Select(item => (item.Number, item.Title)), actions.Select(item => (item.Action, item.Note, item.CreatedAt)), hearings.Select(item => (item.StartsAt, item.Room)));

        using var generate = BriefingTelemetry.Source.StartActivity("generate");
        generate?.SetTag("gen_ai.request.model", MiniMaxClient.ModelName);
        var completion = await miniMax.CompleteAsync(SystemPrompt, packet, cancellationToken);
        generate?.SetTag("gen_ai.usage.input_tokens", completion.InputTokens);
        generate?.SetTag("gen_ai.usage.output_tokens", completion.OutputTokens);
        if (string.IsNullOrWhiteSpace(completion.Content))
        {
            return new BriefingOutcome(BriefingKind.Empty, Error: "The model returned an empty brief.");
        }

        using var citation = BriefingTelemetry.Source.StartActivity("citation");
        var unknown = CitationCheck.Unknown(completion.Content, allowed);
        citation?.SetTag("citation.passed", unknown.Count == 0);
        var briefing = new AiBriefing
        {
            Id = Guid.NewGuid(),
            CommitteeId = scope.CommitteeId,
            BillId = bill.Id,
            ActorUserId = scope.UserId,
            Model = MiniMaxClient.ModelName,
            PromptVersion = PromptVersion,
            RetrievedBillNumbers = string.Join(", ", allowed.OrderBy(number => number)),
            DraftText = completion.Content,
            InputTokens = completion.InputTokens,
            OutputTokens = completion.OutputTokens,
            CreatedAt = DateTime.UtcNow,
        };

        if (unknown.Count > 0)
        {
            briefing.Status = BriefingStatuses.Withheld;
            briefing.WithholdReason = $"Cited {string.Join(", ", unknown)}, which is outside this committee.";
            db.AiBriefings.Add(briefing);
            db.AuditEvents.Add(Audit(briefing, Actions.DraftWithheld, $"{bill.Number} brief withheld: {briefing.WithholdReason}"));
            await db.SaveChangesAsync(cancellationToken);
            return new BriefingOutcome(BriefingKind.Withheld, briefing.Id, Error: "The draft cited a bill outside this committee.");
        }

        briefing.Status = BriefingStatuses.Pending;
        db.AiBriefings.Add(briefing);
        db.AuditEvents.Add(Audit(briefing, Actions.BriefingRequested, $"{bill.Number} brief requested"));
        await db.SaveChangesAsync(cancellationToken);
        return new BriefingOutcome(BriefingKind.Ready, briefing.Id, completion.Content, MiniMaxClient.ModelName, PromptVersion);
    }

    public async Task<BriefingOutcome> DecideAsync(Guid billId, Guid briefingId, string decision, string? note, CancellationToken cancellationToken)
    {
        var briefing = await db.AiBriefings.FirstOrDefaultAsync(item =>
            item.Id == briefingId && item.BillId == billId && item.CommitteeId == scope.CommitteeId, cancellationToken);
        if (briefing is null)
        {
            return new BriefingOutcome(BriefingKind.NotFound);
        }

        if (briefing.Status != BriefingStatuses.Pending)
        {
            return new BriefingOutcome(BriefingKind.Conflict, Error: "This brief was already decided.");
        }

        var bill = await db.Bills.FirstAsync(item => item.Id == billId, cancellationToken);
        var now = DateTime.UtcNow;
        if (decision == "discard")
        {
            briefing.Status = BriefingStatuses.Discarded;
            briefing.DecidedAt = now;
            db.AuditEvents.Add(Audit(briefing, Actions.DraftDiscarded, $"{bill.Number} brief discarded"));
            await db.SaveChangesAsync(cancellationToken);
            return new BriefingOutcome(BriefingKind.Ready, briefing.Id);
        }

        if (decision != "accept")
        {
            return new BriefingOutcome(BriefingKind.Conflict, Error: "Decision must be accept or discard.");
        }

        var accepted = string.IsNullOrWhiteSpace(note) ? briefing.DraftText : note.Trim();
        var edited = !string.Equals(accepted, briefing.DraftText, StringComparison.Ordinal);
        briefing.Status = BriefingStatuses.Accepted;
        briefing.AcceptedText = accepted;
        briefing.DecidedAt = now;
        bill.LastActionText = $"{bill.Number} staff note added";
        bill.UpdatedAt = now;
        db.BillActions.Add(new BillAction
        {
            Id = Guid.NewGuid(),
            BillId = bill.Id,
            ActorUserId = scope.UserId,
            Action = Actions.NoteAdded,
            Note = accepted,
            CreatedAt = now,
        });
        var detail = edited
            ? $"{bill.Number} staff note added from briefing {briefing.Id} (edited)"
            : $"{bill.Number} staff note added from briefing {briefing.Id}";
        db.AuditEvents.Add(Audit(briefing, Actions.NoteAdded, detail));
        await db.SaveChangesAsync(cancellationToken);
        return new BriefingOutcome(BriefingKind.Ready, briefing.Id);
    }

    private static string BuildPacket(
        Bill bill,
        IEnumerable<(string Number, string Title)> committeeBills,
        IEnumerable<(string Action, string? Note, DateTime CreatedAt)> actions,
        IEnumerable<(DateTime StartsAt, string Room)> hearings)
    {
        var siblings = string.Join("\n", committeeBills.Select(item => $"- {item.Number}: {item.Title}"));
        var history = string.Join("\n", actions.Select(item => $"- {item.CreatedAt:yyyy-MM-dd} {item.Action} {item.Note}".Trim()));
        var rooms = string.Join("\n", hearings.Select(item => $"- {item.StartsAt:yyyy-MM-dd HH:mm} UTC room {item.Room}"));
        return $"""
            Bill: {bill.Number}
            Title: {bill.Title}
            Sponsor: {bill.Sponsor}
            Status: {bill.Status}
            Last action: {bill.LastActionText}

            Bills in this committee:
            {siblings}

            Hearings linked to this bill:
            {(string.IsNullOrWhiteSpace(rooms) ? "- none" : rooms)}

            Recent actions:
            {(string.IsNullOrWhiteSpace(history) ? "- none" : history)}
            """;
    }

    private AuditEvent Audit(AiBriefing briefing, string action, string detail) => new()
    {
        Id = Guid.NewGuid(),
        CommitteeId = scope.CommitteeId,
        ActorUserId = scope.UserId,
        BillId = briefing.BillId,
        Action = action,
        Detail = detail,
        TraceId = briefing.Id,
        CreatedAt = DateTime.UtcNow,
    };
}
