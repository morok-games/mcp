using Microsoft.AspNetCore.Mvc;
using Platform.Api.Catalog.Entities;
using Platform.Api.Catalog.Ingestion;

namespace Platform.Api.Controllers;

[ApiController]
[Route("api/catalog/import")]
public class CatalogImportController(CatalogIngestionService ingestion, ILogger<CatalogImportController> logger) : ControllerBase
{
    public record ImportRequest(
        SourcePlatform Platform,
        string BusinessSlug,
        Uri? Url,
        Dictionary<string, string>? Settings);

    public record ImportResponse(CatalogSnapshot Snapshot, CatalogImportResult? Result);

    /// <summary>
    /// Imports a business catalog from an external source. With dryRun=true the normalized snapshot
    /// (including validation warnings) is returned without saving.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ImportResponse>> Import([FromBody] ImportRequest request, [FromQuery] bool dryRun, CancellationToken ct)
    {
        var source = new CatalogSource(request.Platform, request.BusinessSlug, request.Url, request.Settings);

        try
        {
            if (dryRun)
                return new ImportResponse(await ingestion.FetchAsync(source, ct), null);

            var (snapshot, result) = await ingestion.IngestAsync(source, ct);
            return new ImportResponse(snapshot, result);
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            logger.LogError(ex, "Catalog import failed for {Business}", request.BusinessSlug);
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
