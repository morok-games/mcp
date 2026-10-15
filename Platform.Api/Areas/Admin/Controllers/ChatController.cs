using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Platform.Api.Areas.Admin.Controllers;

/// <summary>Test playground for the assistant: the chat UI over /api/chat, available to admins only.</summary>
[Area("Admin")]
[Authorize]
public class ChatController : Controller
{
    public IActionResult Index() => View();
}
