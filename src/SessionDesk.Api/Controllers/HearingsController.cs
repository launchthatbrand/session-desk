using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SessionDesk.Api.Auth;
using SessionDesk.Api.Data;

namespace SessionDesk.Api.Controllers;

[ApiController]
[Authorize(Policy = "CommitteeMember")]
[Route("api/hearings")]
public sealed class HearingsController(AppDbContext db, CommitteeContext scope) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var hearings = await db.Hearings.AsNoTracking()
            .Where(hearing => hearing.CommitteeId == scope.CommitteeId)
            .OrderBy(hearing => hearing.StartsAt)
            .ToListAsync();
        var hearingIds = hearings.Select(hearing => hearing.Id).ToList();
        var links = await db.BillActions.AsNoTracking()
            .Where(action => action.HearingId != null && hearingIds.Contains(action.HearingId.Value) && action.Action == Actions.PlacedOnAgenda)
            .Join(db.Bills.AsNoTracking(), action => action.BillId, bill => bill.Id, (action, bill) => new { action.HearingId, bill.Id, bill.Number, bill.Title })
            .ToListAsync();

        return Ok(hearings.Select(hearing => new HearingResponse(
            hearing.Id,
            hearing.StartsAt,
            hearing.Room,
            links.Where(link => link.HearingId == hearing.Id)
                .Select(link => new HearingBill(link.Id, link.Number, link.Title))
                .DistinctBy(link => link.Id)
                .ToList())));
    }

    [HttpPost]
    [Authorize(Policy = "CommitteeClerk")]
    public async Task<IActionResult> Schedule(ScheduleHearingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Room))
        {
            return BadRequest(new { error = "Room is required." });
        }

        var startsAt = DateTime.SpecifyKind(request.StartsAt, DateTimeKind.Utc);
        var hearing = new Hearing
        {
            Id = Guid.NewGuid(),
            CommitteeId = scope.CommitteeId,
            StartsAt = startsAt,
            Room = request.Room.Trim(),
        };
        var when = startsAt.ToString("MMM d", CultureInfo.InvariantCulture);
        db.Hearings.Add(hearing);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            CommitteeId = scope.CommitteeId,
            ActorUserId = scope.UserId,
            Action = Actions.HearingScheduled,
            Detail = $"Hearing scheduled for {when} in room {hearing.Room}",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return Ok(new HearingResponse(hearing.Id, hearing.StartsAt, hearing.Room, []));
    }
}

public sealed record HearingBill(Guid Id, string Number, string Title);

public sealed record HearingResponse(Guid Id, DateTime StartsAt, string Room, IReadOnlyList<HearingBill> Bills);

public sealed record ScheduleHearingRequest(DateTime StartsAt, string Room);
