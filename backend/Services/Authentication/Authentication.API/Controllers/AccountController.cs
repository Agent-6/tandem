using Authentication.API.Identity;
using Authentication.API.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Authentication.API.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet("Account/Login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginModel());
    }

    [HttpPost("Account/Login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (ModelState.IsValid)
        {
            var user = await _userManager.FindByEmailAsync(model.Email!);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(model);
            }

            // Verify password
            var passwordValid = await _userManager.CheckPasswordAsync(user, model.Password!);
            if (!passwordValid)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(model);
            }

            // Create claims principal
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Name, user.UserName!),
                new(ClaimTypes.Email, user.Email!),
                new("displayName", user.DisplayName ?? ""),
                new("createdAt", user.CreatedAt.ToString("o")),
            };

            // Add roles
            var roles = await _userManager.GetRolesAsync(user);
            foreach (var role in roles)
            {
                claims.Add(new Claim("roles", role));
            }

            var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
            
            // Accumulate multiple identities for switching
            var identities = new List<ClaimsIdentity> { identity };
            if (User?.Identity?.IsAuthenticated == true)
            {
                foreach (var existingIdentity in User.Identities)
                {
                    var existingEmail = existingIdentity.FindFirst(ClaimTypes.Email)?.Value;
                    if (existingEmail != null && !existingEmail.Equals(user.Email, StringComparison.OrdinalIgnoreCase))
                    {
                        identities.Add(existingIdentity);
                    }
                }
            }
            var principal = new ClaimsPrincipal(identities);

            await HttpContext.SignInAsync(
                IdentityConstants.ApplicationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(model.RememberMe ? 14 : 1),
                    RedirectUri = returnUrl ?? Url.Content("~/")
                });

            _logger.LogInformation("User {Email} logged in successfully.", model.Email);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return Redirect("~/");
        }

        return View(model);
    }

    [HttpGet("Account/Register")]
    public IActionResult Register()
    {
        return View(new RegisterModel());
    }

    [HttpPost("Account/Register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterModel model)
    {
        if (ModelState.IsValid)
        {
            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                DisplayName = model.DisplayName,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (result.Succeeded)
            {
                // Assign default role
                await _userManager.AddToRoleAsync(user, "User");

                // Auto-sign in after registration
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.Id),
                    new(ClaimTypes.Name, user.UserName!),
                    new(ClaimTypes.Email, user.Email!),
                    new("displayName", user.DisplayName ?? ""),
                    new("createdAt", user.CreatedAt.ToString("o")),
                };

                var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
                
                // Accumulate multiple identities for switching
                var identities = new List<ClaimsIdentity> { identity };
                if (User?.Identity?.IsAuthenticated == true)
                {
                    foreach (var existingIdentity in User.Identities)
                    {
                        var existingEmail = existingIdentity.FindFirst(ClaimTypes.Email)?.Value;
                        if (existingEmail != null && !existingEmail.Equals(user.Email, StringComparison.OrdinalIgnoreCase))
                        {
                            identities.Add(existingIdentity);
                        }
                    }
                }
                var principal = new ClaimsPrincipal(identities);

                await HttpContext.SignInAsync(
                    IdentityConstants.ApplicationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14),
                        RedirectUri = "~/"
                    });

                _logger.LogInformation("User {Email} registered successfully.", model.Email);

                return Redirect("~/");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        return View(model);
    }

    [HttpGet("Account/Switcher")]
    public IActionResult Switcher(string returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        var currentPrincipal = HttpContext.User;
        if (currentPrincipal?.Identity?.IsAuthenticated != true)
        {
            return RedirectToAction("Login", new { returnUrl });
        }

        var identities = currentPrincipal.Identities.Where(i => i.IsAuthenticated).ToList();
        return View(identities);
    }

    [HttpPost("Account/SwitchAccount")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchAccount(string email, string returnUrl = null)
    {
        var currentPrincipal = HttpContext.User;
        if (currentPrincipal?.Identity?.IsAuthenticated == true)
        {
            var identities = currentPrincipal.Identities.ToList();
            var targetIdentity = identities.FirstOrDefault(i =>
                i.FindFirst(ClaimTypes.Email)?.Value == email);

            if (targetIdentity != null)
            {
                identities.Remove(targetIdentity);
                identities.Insert(0, targetIdentity);

                await HttpContext.SignInAsync(
                    IdentityConstants.ApplicationScheme,
                    new ClaimsPrincipal(identities));
            }
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return Redirect("~/");
    }

    [HttpGet("Account/Logout")]
    public IActionResult Logout(string? returnId)
    {
        return View();
    }

    [HttpPost("Account/Logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogoutConfirm()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        _logger.LogInformation("User logged out.");
        return Redirect("~/");
    }

    [HttpGet("Account/ForgotPassword")]
    public IActionResult ForgotPassword() => View();

    [HttpPost("Account/ForgotPassword")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword([FromForm] string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            ModelState.AddModelError(string.Empty, "Email is required.");
            return View();
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user != null)
        {
            // In a real app, send password reset email
            // var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            // var callbackUrl = Url.ResetPasswordCallbackLink(...);
            // await _emailService.SendPasswordResetEmail(email, callbackUrl);
        }

        return View("ForgotPasswordConfirmation");
    }

    [HttpGet("Account/ResetPassword")]
    public IActionResult ResetPassword(string? email, string token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            return BadRequest("A token and email must be supplied for password reset.");

        return View(new ResetPasswordRequest { Email = email, Token = token });
    }

    [HttpPost("Account/ResetPassword")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword([FromForm] ResetPasswordRequest request)
    {
        if (!ModelState.IsValid)
            return View(request);

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Failed to reset password. The token may be expired or invalid.");
            return View(request);
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (result.Succeeded)
            return View("ResetPasswordConfirmation");

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(request);
    }
}

public class ResetPasswordRequest
{
    public string? Email { get; set; }
    public string? Token { get; set; }
    public string? NewPassword { get; set; }
}
