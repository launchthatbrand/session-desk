namespace SessionDesk.Api.Auth;

public sealed class CommitteeContext
{
    public bool IsResolved { get; set; }
    public Guid UserId { get; set; }
    public Guid CommitteeId { get; set; }
    public string Role { get; set; } = "";
}
