namespace SessionDesk.Api.Data;

public static class Roles
{
    public const string Clerk = "clerk";
    public const string Analyst = "analyst";
    public const string Viewer = "viewer";
}

public static class BillStatuses
{
    public const string Referred = "referred";
    public const string OnAgenda = "on_agenda";
    public const string ReportedFavorably = "reported_favorably";
    public const string ReportedUnfavorably = "reported_unfavorably";
    public const string TemporarilyPostponed = "temporarily_postponed";
}

public static class Actions
{
    public const string PlacedOnAgenda = "bill.placed_on_agenda";
    public const string ReportedFavorably = "bill.reported_favorably";
    public const string ReportedUnfavorably = "bill.reported_unfavorably";
    public const string Postponed = "bill.postponed";
    public const string NoteAdded = "bill.note_added";
    public const string HearingScheduled = "hearing.scheduled";
    public const string Referred = "bill.referred";
}

public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class Committee
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Assignment
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CommitteeId { get; set; }
    public string Role { get; set; } = "";
    public User User { get; set; } = null!;
    public Committee Committee { get; set; } = null!;
}

public sealed class Bill
{
    public Guid Id { get; set; }
    public Guid CommitteeId { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Sponsor { get; set; } = "";
    public string Status { get; set; } = BillStatuses.Referred;
    public string LastActionText { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public Committee Committee { get; set; } = null!;
}

public sealed class Hearing
{
    public Guid Id { get; set; }
    public Guid CommitteeId { get; set; }
    public DateTime StartsAt { get; set; }
    public string Room { get; set; } = "";
    public Committee Committee { get; set; } = null!;
}

public sealed class BillAction
{
    public Guid Id { get; set; }
    public Guid BillId { get; set; }
    public Guid? HearingId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public Bill Bill { get; set; } = null!;
    public Hearing? Hearing { get; set; }
    public User Actor { get; set; } = null!;
}

public sealed class AuditEvent
{
    public Guid Id { get; set; }
    public Guid CommitteeId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? BillId { get; set; }
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public User Actor { get; set; } = null!;
    public Bill? Bill { get; set; }
}
