using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SessionDesk.Api.Data;
using SessionDesk.Api.Search;

namespace SessionDesk.Api.Controllers;

[ApiController]
[Authorize(Policy = "CommitteeMember")]
[Route("api/search")]
public sealed class SearchController(IBillSearch search, IConfiguration configuration, AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Query([FromQuery] string? q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["OpenSearch:Url"]))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Search is not configured." });
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(Array.Empty<SearchHit>());
        }

        try
        {
            var hits = await search.SearchAsync(q.Trim(), cancellationToken);
            return Ok(hits);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Search is unavailable." });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id, CancellationToken cancellationToken)
    {
        var bill = await db.Bills.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new SearchBill(item.Id, item.Number, item.Title, item.Sponsor, item.CommitteeId, item.Committee.Name, item.Citation, item.Body))
            .FirstOrDefaultAsync(cancellationToken);
        return bill is null ? NotFound() : Ok(bill);
    }
}
