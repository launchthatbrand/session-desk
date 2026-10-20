using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SessionDesk.Api.Auth;
using SessionDesk.Api.Data;

namespace SessionDesk.Api.Controllers;

[ApiController]
[Authorize(Policy = "CommitteeMember")]
[Route("api/audit")]
public sealed class AuditController(AppDbContext db, CommitteeContext scope) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var events = await db.AuditEvents.AsNoTracking()
            .Where(item => item.CommitteeId == scope.CommitteeId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(100)
            .Select(item => new AuditResponse(
                item.Id,
                item.Bill != null ? item.Bill.Number : null,
                item.Detail,
                item.Actor.DisplayName,
                item.Action,
                item.CreatedAt))
            .ToListAsync();
        return Ok(events);
    }
}

public sealed record AuditResponse(
    Guid Id,
    string? BillNumber,
    string Detail,
    string Actor,
    string Action,
    DateTime CreatedAt);
