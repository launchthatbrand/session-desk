using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SessionDesk.Api.Data;

namespace SessionDesk.Api.Auth;

public sealed class CommitteeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db, CommitteeContext scope)
    {
        if (!context.Request.Path.StartsWithSegments("/api")
            || context.Request.Path.StartsWithSegments("/api/health")
            || context.Request.Path.StartsWithSegments("/api/auth/login"))
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var header = context.Request.Headers["X-Committee-Id"].ToString();
        if (!Guid.TryParse(subject, out var userId) || !Guid.TryParse(header, out var committeeId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var assignment = await db.Assignments.AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId && item.CommitteeId == committeeId);
        if (assignment is null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        scope.IsResolved = true;
        scope.UserId = userId;
        scope.CommitteeId = committeeId;
        scope.Role = assignment.Role;
        await next(context);
    }
}
