using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Trace;
using SessionDesk.Api.Auth;
using SessionDesk.Api.Briefing;
using SessionDesk.Api.Data;
using SessionDesk.Api.Search;
using SessionDesk.Api.Seed;

var builder = WebApplication.CreateBuilder(args);

var signingKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");
}

if (string.IsNullOrWhiteSpace(builder.Configuration["Seed:AdminPassword"]))
{
    throw new InvalidOperationException("Seed:AdminPassword is required.");
}

builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
        .UseSnakeCaseNamingConvention());
builder.Services.AddScoped<CommitteeContext>();
builder.Services.AddHttpClient<IMiniMaxClient, MiniMaxClient>();
builder.Services.AddHttpClient<OpenSearchBillSearch>();
builder.Services.AddScoped<IBillSearch>(services => services.GetRequiredService<OpenSearchBillSearch>());
builder.Services.AddHostedService<BillIndexService>();
builder.Services.AddScoped<BriefingService>();
builder.Services.AddSingleton<JwtTokenService>();
var langfusePublic = builder.Configuration["Langfuse:PublicKey"];
var langfuseSecret = builder.Configuration["Langfuse:SecretKey"];
var langfuseBase = builder.Configuration["Langfuse:BaseUrl"];
if (!string.IsNullOrWhiteSpace(langfusePublic)
    && !string.IsNullOrWhiteSpace(langfuseSecret)
    && !string.IsNullOrWhiteSpace(langfuseBase))
{
    var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{langfusePublic}:{langfuseSecret}"));
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .AddSource(BriefingTelemetry.Name)
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri($"{langfuseBase.TrimEnd('/')}/api/public/otel");
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                options.Headers = $"Authorization=Basic {basic}";
            }));
}
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CommitteeMember", policy =>
        policy.RequireAuthenticatedUser().AddRequirements(new CommitteeMemberRequirement()));
    options.AddPolicy("CommitteeClerk", policy =>
        policy.RequireAuthenticatedUser().AddRequirements(new CommitteeClerkRequirement()));
});
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, CommitteeMemberHandler>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, CommitteeClerkHandler>();

var app = builder.Build();
app.UseAuthentication();
app.UseMiddleware<CommitteeMiddleware>();
app.UseAuthorization();
app.MapControllers();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    for (var attempt = 1; attempt <= 30; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            break;
        }
        catch (Exception) when (attempt < 30)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    await SeedData.EnsureReadyAsync(scope.ServiceProvider);
}

app.Run();

public partial class Program;
