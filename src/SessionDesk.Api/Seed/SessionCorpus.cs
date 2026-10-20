using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SessionDesk.Api.Data;

namespace SessionDesk.Api.Seed;

public static class SessionCorpus
{
    public const int Count = 300;
    public const string FirstNumber = "SB 2000";

    private static readonly string[] Committees = ["Appropriations", "Rules", "Education", "Health Policy", "Judiciary", "Agriculture"];
    private static readonly string[] Sponsors = ["Sen. Hale", "Sen. Ortiz", "Sen. Cole", "Sen. Brooks", "Sen. Patel", "Sen. Nguyen", "Sen. Adler", "Sen. Brooks"];
    private static readonly string[] Statuses = [BillStatuses.Referred, BillStatuses.OnAgenda, BillStatuses.ReportedFavorably, BillStatuses.TemporarilyPostponed];
    private static readonly string[] Citations = ["120.54", "119.071", "286.011", "1003.42", "381.986", "373.459", "161.041", "420.507", "282.318", "11.242"];
    private static readonly string[] Subjects =
    [
        "harbor marker repair",
        "school nurse schedules",
        "county road debris",
        "clinic license renewal",
        "courthouse filing hours",
        "citrus inspection fees",
        "public notice portals",
        "storm drain mapping",
        "library broadband grants",
        "veteran hiring reports",
    ];

    public static async Task EnsureAsync(AppDbContext db)
    {
        await BackfillKnownBills(db);
        if (await db.Bills.AnyAsync(bill => bill.Number == FirstNumber))
        {
            return;
        }

        var random = new Random(2027);
        var committees = new Dictionary<string, Committee>();
        foreach (var name in Committees)
        {
            var existing = await db.Committees.FirstOrDefaultAsync(committee => committee.Name == name);
            if (existing is null)
            {
                existing = new Committee { Id = IdFor($"committee:{name}"), Name = name };
                db.Committees.Add(existing);
            }

            committees[name] = existing;
        }

        var bills = new List<Bill>(Count);
        for (var index = 0; index < Count; index++)
        {
            var number = $"SB {2000 + index}";
            var committeeName = Committees[index % Committees.Length];
            var subject = Subjects[random.Next(Subjects.Length)];
            var citation = Citations[random.Next(Citations.Length)];
            var sponsor = Sponsors[random.Next(Sponsors.Length)];
            var status = Statuses[random.Next(Statuses.Length)];
            var title = $"{char.ToUpper(subject[0])}{subject[1..]}";
            bills.Add(new Bill
            {
                Id = IdFor($"bill:{number}"),
                CommitteeId = committees[committeeName].Id,
                Number = number,
                Title = title,
                Sponsor = sponsor,
                Status = status,
                Citation = citation,
                Body = $"This bill concerns {subject} in {committeeName}. It cites section {citation} and directs the agency to publish a report for the session.",
                LastActionText = $"{number} referred to {committeeName}",
                UpdatedAt = new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc).AddMinutes(index),
            });
        }

        db.Bills.AddRange(bills);
        await db.SaveChangesAsync();
    }

    private static async Task BackfillKnownBills(AppDbContext db)
    {
        var known = new Dictionary<string, (string Citation, string Body)>
        {
            ["SB 1200"] = ("373.459", "Creates a grant program for regional water storage. The bill cites section 373.459 and requires an annual public report."),
            ["SB 1204"] = ("1006.25", "Sets a school bus inspection cycle for district fleets. The bill cites section 1006.25 and keeps the current safety standards."),
            ["SB 1210"] = ("282.318", "Requires cities to report cyber incidents to the state. The bill cites section 282.318 and limits the report to operational impact."),
            ["SB 1218"] = ("161.041", "Sets timelines for coastal construction permits. The bill cites section 161.041 and does not change local zoning."),
            ["SB 1302"] = ("11.242", "Corrects publication errors in the legislative rules. The bill cites section 11.242 and makes no substantive policy change."),
        };
        var numbers = known.Keys.ToArray();
        var bills = await db.Bills.Where(bill => numbers.Contains(bill.Number) && bill.Citation == "").ToListAsync();
        foreach (var bill in bills)
        {
            var fill = known[bill.Number];
            bill.Citation = fill.Citation;
            bill.Body = fill.Body;
        }

        if (bills.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }

    public static Guid BillId(string number) => IdFor($"bill:{number}");

    private static Guid IdFor(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }
}
