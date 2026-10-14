using Microsoft.AspNetCore.Mvc;
using Platform.Api.Assistant;
using Platform.Api.Models;
using Platform.Api.Services;

namespace Platform.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(ILlmClient llm, IQueryParser queryParser, ILogger<ChatController> logger) : ControllerBase
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

    private static bool EndsWithUserMessage(ChatRequest request) =>
        request.Messages is { Count: > 0 } && request.Messages[^1].Role == "user";
}
