using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Admin;
using Platform.Api.Areas.Admin.Models;

namespace Platform.Api.Areas.Admin.Controllers;

[Area("Admin")]
[AutoValidateAntiforgeryToken]
public class AccountController(
    AdminDbContext db,
    IPasswordHasher<AdminUser> hasher,
    ILogger<AccountController> logger) : Controller
{
    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl) => View(new LoginModel { ReturnUrl = returnUrl });

    // Slows down password guessing: limited attempts per IP per minute
    [HttpPost, AllowAnonymous, EnableRateLimiting(RateLimits.Login)]
    public async Task<IActionResult> Login(LoginModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(model);

        var username = AdminUser.Normalize(model.Username);
        var admin = await db.Admins.FirstOrDefaultAsync(a => a.Username == username, ct);

        var result = admin is null
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(admin, admin.PasswordHash, model.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Failed admin login for {Username}", username);
            // Same message for unknown user and wrong password: do not reveal which accounts exist
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View(model);
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            admin!.PasswordHash = hasher.HashPassword(admin, model.Password);

        admin!.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
                new Claim(ClaimTypes.Name, admin.Username)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        logger.LogInformation("Admin {Username} signed in", admin.Username);

        // Only local return URLs: prevents open redirects to other sites
        return Url.IsLocalUrl(model.ReturnUrl)
            ? LocalRedirect(model.ReturnUrl)
            : RedirectToAction("Index", "Businesses");
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
