using System.Diagnostics;

namespace SessionDesk.Api.Briefing;

public static class BriefingTelemetry
{
    public const string Name = "SessionDesk.Briefing";

    public static readonly ActivitySource Source = new(Name);
}
