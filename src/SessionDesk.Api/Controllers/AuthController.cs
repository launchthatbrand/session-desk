using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SessionDesk.Api.Auth;
using SessionDesk.Api.Data;

namespace SessionDesk.Api.Controllers;

[ApiController]
public sealed class AuthController(AppDbContext db, IPasswordHasher<User> hasher, JwtTokenService tokens) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("/api/auth/login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(item => item.Email == email);
        if (user is null
            || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            return Unauthorized();
        }

        var committees = await db.Assignments.AsNoTracking()
            .Where(assignment => assignment.UserId == user.Id)
            .OrderBy(assignment => assignment.Committee.Name)
            .Select(assignment => new CommitteeResponse(assignment.CommitteeId, assignment.Committee.Name, assignment.Role))
            .ToListAsync();

        return Ok(new LoginResponse(
            tokens.Create(user),
            new UserResponse(user.Id, user.Email, user.DisplayName),
            committees));
    }

    [AllowAnonymous]
    [HttpGet("/api/health")]
    public IActionResult Health() => Ok(new { status = "ok" });
}

public sealed record LoginRequest(string Email, string Password);

public sealed record UserResponse(Guid Id, string Email, string DisplayName);

public sealed record CommitteeResponse(Guid Id, string Name, string Role);

public sealed record LoginResponse(string Token, UserResponse User, IReadOnlyList<CommitteeResponse> Committees);
