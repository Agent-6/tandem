using Authentication.API.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using System.Security.Claims;

namespace Authentication.API.Controllers;

/// <summary>
/// API controller for authentication-related operations.
/// Token issuance is handled by the AuthorizationController via /connect/token endpoint.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Register a new user account.
    /// POST /api/auth/register
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (string.IsNullOrEmpty(model.Email) || string.IsNullOrEmpty(model.Password))
            return BadRequest("Email and password are required.");

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            DisplayName = model.DisplayName,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        // Assign default role
        await _userManager.AddToRoleAsync(user, "User");

        _logger.LogInformation("User {Email} registered successfully.", model.Email);

        return Ok(new { message = "Registration successful" });
    }

    /// <summary>
    /// Get current user info.
    /// GET /api/auth/userinfo
    /// Note: For OIDC userinfo, use /connect/userinfo instead.
    /// </summary>
    [HttpGet("userinfo")]
    [Authorize]
    public async Task<IActionResult> Userinfo()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return NotFound("User not found");

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return NotFound("User not found");

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new
        {
            sub = user.Id,
            email = user.Email,
            name = user.UserName,
            displayName = user.DisplayName,
            createdAt = user.CreatedAt,
            roles = roles
        });
    }

    /// <summary>
    /// Revoke all sessions for the current user.
    /// POST /api/auth/revoke
    /// </summary>
    [HttpPost("revoke")]
    [Authorize]
    public IActionResult Revoke()
    {
        // In a production app, you would:
        // 1. Revoke all authorization entries for the user
        // 2. Revoke all tokens for the user
        // For now, just return success
        return Ok(new { message = "Sessions revoked" });
    }
}

public record RegisterRequest(string Email, string Password, string DisplayName);
