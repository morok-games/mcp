using Microsoft.AspNetCore.Mvc;
using Platform.Api.Models;
using Platform.Api.Services;

namespace Platform.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(ILlmClient llm, ILogger<ChatController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ChatResponse>> Post([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (request.Messages is not { Count: > 0 } || request.Messages[^1].Role != "user")
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
}
