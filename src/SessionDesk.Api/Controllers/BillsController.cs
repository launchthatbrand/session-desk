using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SessionDesk.Api.Auth;
using SessionDesk.Api.Briefing;
using SessionDesk.Api.Data;

namespace SessionDesk.Api.Controllers;

[ApiController]
[Authorize(Policy = "CommitteeMember")]
[Route("api/bills")]
public sealed class BillsController(AppDbContext db, CommitteeContext scope, BriefingService briefing) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? q)
    {
        var query = db.Bills.AsNoTracking().Where(bill => bill.CommitteeId == scope.CommitteeId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(bill => bill.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(bill =>
                bill.Number.ToLower().Contains(term)
                || bill.Title.ToLower().Contains(term)
                || bill.Sponsor.ToLower().Contains(term));
        }

        var bills = await query
            .OrderByDescending(bill => bill.UpdatedAt)
            .Select(bill => new BillResponse(
                bill.Id,
                bill.Number,
                bill.Title,
                bill.Sponsor,
                bill.Status,
                bill.LastActionText,
                bill.UpdatedAt))
            .ToListAsync();
        return Ok(bills);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var (statuses, days) = await BillSummaryQuery.LoadAsync(db, scope.CommitteeId, cancellationToken);
        int Count(string status) => statuses.FirstOrDefault(item => item.Status == status)?.Count ?? 0;
        return Ok(new SummaryResponse(
            Count(BillStatuses.Referred),
            Count(BillStatuses.OnAgenda),
            Count(BillStatuses.ReportedFavorably),
            Count(BillStatuses.ReportedUnfavorably),
            Count(BillStatuses.TemporarilyPostponed),
            days.Select(day => new DayBucket(day.Day.ToString("yyyy-MM-dd"), day.Action, day.Count)).ToList()));
    }

    [HttpPost("{id:guid}/brief")]
    public async Task<IActionResult> Brief(Guid id, CancellationToken cancellationToken)
    {
        if (scope.Role == Roles.Viewer)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var outcome = await briefing.RequestAsync(id, cancellationToken);
        return FromBriefing(outcome, includeDraft: true);
    }

    [HttpPost("{id:guid}/brief/{briefingId:guid}")]
    public async Task<IActionResult> Decide(Guid id, Guid briefingId, BriefDecisionRequest request, CancellationToken cancellationToken)
    {
        if (scope.Role == Roles.Viewer)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var outcome = await briefing.DecideAsync(id, briefingId, request.Decision, request.Note, cancellationToken);
        return FromBriefing(outcome, includeDraft: false);
    }

    [HttpPost("{id:guid}/actions")]
    public async Task<IActionResult> Record(Guid id, RecordActionRequest request)
    {
        if (scope.Role == Roles.Viewer)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var isNote = request.Action == Actions.NoteAdded;
        if (scope.Role == Roles.Analyst && !isNote)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (request.Action is not (
            Actions.PlacedOnAgenda
            or Actions.ReportedFavorably
            or Actions.ReportedUnfavorably
            or Actions.Postponed
            or Actions.NoteAdded))
        {
            return BadRequest(new { error = "Unknown action." });
        }

        var bill = await db.Bills.FirstOrDefaultAsync(item => item.Id == id && item.CommitteeId == scope.CommitteeId);
        if (bill is null)
        {
            return NotFound();
        }

        Hearing? hearing = null;
        if (request.Action == Actions.PlacedOnAgenda)
        {
            if (request.HearingStartsAt is null || string.IsNullOrWhiteSpace(request.Room))
            {
                return BadRequest(new { error = "Hearing date and room are required." });
            }

            var startsAt = DateTime.SpecifyKind(request.HearingStartsAt.Value, DateTimeKind.Utc);
            var room = request.Room.Trim();
            hearing = await db.Hearings.FirstOrDefaultAsync(item =>
                item.CommitteeId == scope.CommitteeId && item.StartsAt == startsAt && item.Room == room);
            if (hearing is null)
            {
                hearing = new Hearing
                {
                    Id = Guid.NewGuid(),
                    CommitteeId = scope.CommitteeId,
                    StartsAt = startsAt,
                    Room = room,
                };
                db.Hearings.Add(hearing);
            }
        }

        var detail = Describe(bill, request.Action, hearing);
        bill.Status = request.Action switch
        {
            Actions.PlacedOnAgenda => BillStatuses.OnAgenda,
            Actions.ReportedFavorably => BillStatuses.ReportedFavorably,
            Actions.ReportedUnfavorably => BillStatuses.ReportedUnfavorably,
            Actions.Postponed => BillStatuses.TemporarilyPostponed,
            _ => bill.Status,
        };
        bill.LastActionText = detail;
        bill.UpdatedAt = DateTime.UtcNow;

        db.BillActions.Add(new BillAction
        {
            Id = Guid.NewGuid(),
            BillId = bill.Id,
            HearingId = hearing?.Id,
            ActorUserId = scope.UserId,
            Action = request.Action,
            Note = request.Note,
            CreatedAt = DateTime.UtcNow,
        });
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            CommitteeId = scope.CommitteeId,
            ActorUserId = scope.UserId,
            BillId = bill.Id,
            Action = request.Action,
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return Ok(new BillResponse(bill.Id, bill.Number, bill.Title, bill.Sponsor, bill.Status, bill.LastActionText, bill.UpdatedAt));
    }

    private IActionResult FromBriefing(BriefingOutcome outcome, bool includeDraft) => outcome.Kind switch
    {
        BriefingKind.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = outcome.Error }),
        BriefingKind.NotFound => NotFound(),
        BriefingKind.Conflict => Conflict(new { error = outcome.Error }),
        BriefingKind.Empty => StatusCode(StatusCodes.Status502BadGateway, new { error = outcome.Error }),
        BriefingKind.Withheld => UnprocessableEntity(new { error = outcome.Error }),
        BriefingKind.Ready when includeDraft => Ok(new BriefResponse(outcome.BriefingId!.Value, outcome.Draft!, outcome.Model!, outcome.PromptVersion!)),
        BriefingKind.Ready => Ok(new { briefingId = outcome.BriefingId }),
        _ => StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static string Describe(Bill bill, string action, Hearing? hearing)
    {
        var when = hearing?.StartsAt.ToString("MMM d", CultureInfo.InvariantCulture);
        return action switch
        {
            Actions.PlacedOnAgenda => $"{bill.Number} placed on the {when} agenda",
            Actions.ReportedFavorably => $"{bill.Number} reported favorably",
            Actions.ReportedUnfavorably => $"{bill.Number} reported unfavorably",
            Actions.Postponed => $"{bill.Number} temporarily postponed",
            Actions.NoteAdded => $"{bill.Number} staff note added",
            _ => bill.Number,
        };
    }
}

public sealed record BillResponse(
    Guid Id,
    string Number,
    string Title,
    string Sponsor,
    string Status,
    string LastAction,
    DateTime UpdatedAt);

public sealed record SummaryResponse(
    int Referred,
    int OnAgenda,
    int ReportedFavorably,
    int ReportedUnfavorably,
    int TemporarilyPostponed,
    IReadOnlyList<DayBucket> Days);

public sealed record DayBucket(string Day, string Action, int Count);

public sealed record RecordActionRequest(string Action, DateTime? HearingStartsAt, string? Room, string? Note);

public sealed record BriefResponse(Guid BriefingId, string Draft, string Model, string PromptVersion);

public sealed record BriefDecisionRequest(string Decision, string? Note);
