using Microsoft.AspNetCore.Authorization;
using SessionDesk.Api.Data;

namespace SessionDesk.Api.Auth;

public sealed class CommitteeMemberRequirement : IAuthorizationRequirement;

public sealed class CommitteeClerkRequirement : IAuthorizationRequirement;

public sealed class CommitteeMemberHandler(CommitteeContext scope)
    : AuthorizationHandler<CommitteeMemberRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CommitteeMemberRequirement requirement)
    {
        if (scope.IsResolved)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public sealed class CommitteeClerkHandler(CommitteeContext scope)
    : AuthorizationHandler<CommitteeClerkRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CommitteeClerkRequirement requirement)
    {
        if (scope.IsResolved && scope.Role == Roles.Clerk)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
