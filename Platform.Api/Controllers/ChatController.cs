using Microsoft.AspNetCore.Mvc;
using Platform.Api.Assistant;
using Platform.Api.Catalog.Search;
using Platform.Api.Models;
using Platform.Api.Services;

namespace Platform.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(
    ILlmClient llm,
    IQueryParser queryParser,
    ICatalogSearch catalogSearch,
    ILogger<ChatController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ChatResponse>> Post([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (!EndsWithUserMessage(request))
            return BadRequest("The last message must be from the user.");

        try
        {
            var reply = await llm.GenerateAsync(request.Messages, ct);
            return new ChatResponse(reply);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            logger.LogError(ex, "LLM call failed");
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>Debug endpoint: shows how the last user message is understood (step 1 of answering).</summary>
    [HttpPost("parse")]
    public async Task<ActionResult<QueryUnderstanding>> Parse([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (!EndsWithUserMessage(request))
            return BadRequest("The last message must be from the user.");

        try
        {
            return await queryParser.ParseAsync(request.Messages, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            logger.LogError(ex, "Query parsing failed");
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    public record SearchDebugResponse(QueryUnderstanding Understanding, CatalogSearchResult? Search);

    /// <summary>Debug endpoint: steps 1 and 2 of answering, the parsed question and what the catalog returns for it.</summary>
    [HttpPost("search")]
    public async Task<ActionResult<SearchDebugResponse>> Search([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (!EndsWithUserMessage(request))
            return BadRequest("The last message must be from the user.");

        try
        {
            var understanding = await queryParser.ParseAsync(request.Messages, ct);
            var search = understanding.NeedsCatalogSearch()
                ? await catalogSearch.SearchAsync(understanding.ToSearchRequest(), ct)
                : null;

            return new SearchDebugResponse(understanding, search);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            logger.LogError(ex, "Query parsing or search failed");
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static bool EndsWithUserMessage(ChatRequest request) =>
        request.Messages is { Count: > 0 } && request.Messages[^1].Role == "user";
}
