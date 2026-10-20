using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SessionDesk.Api.Data;

namespace SessionDesk.Api.Seed;

public static class SeedData
{
    public static async Task EnsureSeededAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        if (await db.Users.AnyAsync())
        {
            return;
        }

        var configuration = services.GetRequiredService<IConfiguration>();
        var password = configuration["Seed:AdminPassword"]
            ?? throw new InvalidOperationException("Seed:AdminPassword is required.");
        var adminEmail = (configuration["Seed:AdminEmail"] ?? "alex.morgan@sessiondesk.local").Trim().ToLowerInvariant();
        var hasher = services.GetRequiredService<IPasswordHasher<User>>();

        var alex = User(adminEmail, "Alex Morgan");
        var patel = User("j.patel@sessiondesk.local", "J. Patel");
        var casey = User("casey.nguyen@sessiondesk.local", "Casey Nguyen");
        alex.PasswordHash = hasher.HashPassword(alex, password);
        patel.PasswordHash = hasher.HashPassword(patel, password);
        casey.PasswordHash = hasher.HashPassword(casey, password);

        var appropriations = new Committee { Id = Guid.NewGuid(), Name = "Appropriations" };
        var rules = new Committee { Id = Guid.NewGuid(), Name = "Rules" };

        db.Users.AddRange(alex, patel, casey);
        db.Committees.AddRange(appropriations, rules);
        db.Assignments.AddRange(
            Assign(alex, appropriations, Roles.Clerk),
            Assign(alex, rules, Roles.Viewer),
            Assign(patel, appropriations, Roles.Analyst),
            Assign(casey, rules, Roles.Viewer));

        var hearing = new Hearing
        {
            Id = Guid.NewGuid(),
            CommitteeId = appropriations.Id,
            StartsAt = new DateTime(2027, 2, 12, 14, 0, 0, DateTimeKind.Utc),
            Room = "412",
        };
        db.Hearings.Add(hearing);

        var water = Bill(appropriations.Id, "SB 1200", "Water storage grants", "Sen. Hale", BillStatuses.Referred, "SB 1200 referred to Appropriations");
        var buses = Bill(appropriations.Id, "SB 1204", "School bus inspection cycle", "Sen. Ortiz", BillStatuses.OnAgenda, "SB 1204 placed on the Feb 12 agenda");
        var cyber = Bill(appropriations.Id, "SB 1210", "Municipal cyber incident reports", "Sen. Cole", BillStatuses.Referred, "SB 1210 referred to Appropriations");
        var coastal = Bill(appropriations.Id, "SB 1218", "Coastal permit timelines", "Sen. Hale", BillStatuses.ReportedFavorably, "SB 1218 reported favorably");
        var publication = Bill(rules.Id, "SB 1302", "Rules publication corrections", "Sen. Brooks", BillStatuses.Referred, "SB 1302 referred to Rules");
        db.Bills.AddRange(water, buses, cyber, coastal, publication);

        var now = DateTime.UtcNow;
        db.BillActions.AddRange(
            ActionRow(water, alex, Actions.Referred, now.AddDays(-6), "Referred to Appropriations"),
            ActionRow(cyber, alex, Actions.Referred, now.AddDays(-4), "Referred to Appropriations"),
            ActionRow(coastal, alex, Actions.ReportedFavorably, now.AddDays(-2), "Reported favorably"),
            ActionRow(buses, alex, Actions.PlacedOnAgenda, now.AddDays(-1), "Placed on the Feb 12 agenda", hearing.Id));

        db.AuditEvents.AddRange(
            Audit(appropriations.Id, alex, water, Actions.Referred, "SB 1200 referred to Appropriations", now.AddDays(-6)),
            Audit(appropriations.Id, alex, cyber, Actions.Referred, "SB 1210 referred to Appropriations", now.AddDays(-4)),
            Audit(appropriations.Id, alex, coastal, Actions.ReportedFavorably, "SB 1218 reported favorably", now.AddDays(-2)),
            Audit(appropriations.Id, alex, buses, Actions.PlacedOnAgenda, "SB 1204 placed on the Feb 12 agenda", now.AddDays(-1)),
            Audit(appropriations.Id, patel, water, Actions.NoteAdded, "SB 1200 staff note added", now.AddHours(-3)),
            Audit(rules.Id, alex, publication, Actions.Referred, "SB 1302 referred to Rules", now.AddDays(-5)));

        await db.SaveChangesAsync();
    }

    private static User User(string email, string name) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        DisplayName = name,
    };

    private static Assignment Assign(User user, Committee committee, string role) => new()
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        CommitteeId = committee.Id,
        Role = role,
    };

    private static Bill Bill(Guid committeeId, string number, string title, string sponsor, string status, string lastAction) => new()
    {
        Id = Guid.NewGuid(),
        CommitteeId = committeeId,
        Number = number,
        Title = title,
        Sponsor = sponsor,
        Status = status,
        LastActionText = lastAction,
        UpdatedAt = DateTime.UtcNow,
    };

    private static BillAction ActionRow(Bill bill, User actor, string action, DateTime createdAt, string note, Guid? hearingId = null) => new()
    {
        Id = Guid.NewGuid(),
        BillId = bill.Id,
        HearingId = hearingId,
        ActorUserId = actor.Id,
        Action = action,
        Note = note,
        CreatedAt = createdAt,
    };

    private static AuditEvent Audit(Guid committeeId, User actor, Bill bill, string action, string detail, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        CommitteeId = committeeId,
        ActorUserId = actor.Id,
        BillId = bill.Id,
        Action = action,
        Detail = detail,
        CreatedAt = createdAt,
    };
}
