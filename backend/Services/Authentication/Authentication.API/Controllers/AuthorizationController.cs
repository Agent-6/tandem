using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Microsoft.AspNetCore.Identity;

namespace Authentication.API.Controllers;

public class AuthorizationController(IOpenIddictScopeManager scopeManager) : Controller
{
    private readonly IOpenIddictScopeManager _scopeManager = scopeManager;

    /// <summary>
    /// Token exchange endpoint - handles /connect/token
    /// </summary>
    [HttpPost("~/connect/token"), Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!result.Succeeded)
        {
            return Challenge(
                properties: null,
                authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        return SignIn(result.Principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Authorization endpoint - handles /connect/authorize
    /// </summary>
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Show account selection if requested
        if (request.HasPromptValue(OpenIddictConstants.PromptValues.SelectAccount))
        {
            var returnUrl = Request.PathBase + Request.Path + QueryString.Create(
                Request.Query.Where(q => q.Key != "prompt").ToList());

            return RedirectToAction("Switcher", "Account", new { returnUrl });
        }

        // Check if user is authenticated via cookies
        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!result.Succeeded || result.Principal?.Identity?.IsAuthenticated != true)
        {
            return Challenge(
                properties: new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + QueryString.Create(Request.Query.ToList())
                },
                authenticationSchemes: [IdentityConstants.ApplicationScheme]);
        }

        // POST request means the user submitted the consent form
        if (HttpMethods.IsPost(Request.Method))
        {
            var form = await Request.ReadFormAsync();
            if (form.ContainsKey("submit.Accept"))
            {
                var identity = new ClaimsIdentity(
                    authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    nameType: ClaimTypes.Name,
                    roleType: ClaimTypes.Role);

                // Add subject claim
                var userId = result.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, userId)
                        .SetDestinations(OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken));
                }

                // Add tenant_id claim if present
                var tenantId = result.Principal.FindFirst("tenant_id")?.Value;
                if (!string.IsNullOrEmpty(tenantId))
                {
                    identity.AddClaim(new Claim("tenant_id", tenantId)
                        .SetDestinations(OpenIddictConstants.Destinations.AccessToken));
                }

                // Add name claim
                var userName = result.Principal.FindFirst(ClaimTypes.Name)?.Value;
                if (!string.IsNullOrEmpty(userName))
                {
                    identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, userName)
                        .SetDestinations(OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken));
                }

                // Add email claim
                var email = result.Principal.FindFirst(ClaimTypes.Email)?.Value;
                if (!string.IsNullOrEmpty(email))
                {
                    identity.AddClaim(new Claim(OpenIddictConstants.Claims.Email, email)
                        .SetDestinations(OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken));
                }

                var principal = new ClaimsPrincipal(identity);
                var scopes = request.GetScopes();
                principal.SetScopes(scopes);

                // For introspection endpoint to work, include the list of resources associated with the requested scopes
                var resources = await _scopeManager.ListResourcesAsync(principal.GetScopes()).ToListAsync();
                principal.SetResources(resources);

                return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            // Denied consent
            return Forbid(
                properties: new AuthenticationProperties()
                {
                    RedirectUri = request.RedirectUri
                },
                authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        // GET request: show the consent view
        return View("Consent", request);
    }

    /// <summary>
    /// UserInfo endpoint - handles /connect/userinfo
    /// </summary>
    [HttpGet("~/connect/userinfo"), HttpPost("~/connect/userinfo")]
    [Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
    public IActionResult GetUserInfo()
    {
        var claims = new Dictionary<string, string?>
        {
            [OpenIddictConstants.Claims.Subject] = User.FindFirst(OpenIddictConstants.Claims.Subject)?.Value,
            [OpenIddictConstants.Claims.Name] = User.FindFirst(OpenIddictConstants.Claims.Name)?.Value,
            [OpenIddictConstants.Claims.GivenName] = User.FindFirst(OpenIddictConstants.Claims.GivenName)?.Value,
            [OpenIddictConstants.Claims.FamilyName] = User.FindFirst(OpenIddictConstants.Claims.FamilyName)?.Value,
            [OpenIddictConstants.Claims.Email] = User.FindFirst(OpenIddictConstants.Claims.Email)?.Value,
            [OpenIddictConstants.Claims.EmailVerified] = User.FindFirst(OpenIddictConstants.Claims.EmailVerified)?.Value,
            [OpenIddictConstants.Claims.PhoneNumber] = User.FindFirst(OpenIddictConstants.Claims.PhoneNumber)?.Value,
            [OpenIddictConstants.Claims.PhoneNumberVerified] = User.FindFirst(OpenIddictConstants.Claims.PhoneNumberVerified)?.Value,
            [OpenIddictConstants.Claims.Address] = User.FindFirst(OpenIddictConstants.Claims.Address)?.Value,
            [OpenIddictConstants.Claims.Gender] = User.FindFirst(OpenIddictConstants.Claims.Gender)?.Value,
            [OpenIddictConstants.Claims.Birthdate] = User.FindFirst(OpenIddictConstants.Claims.Birthdate)?.Value,
            [OpenIddictConstants.Claims.Locality] = User.FindFirst(OpenIddictConstants.Claims.Locality)?.Value,
            [OpenIddictConstants.Claims.PostalCode] = User.FindFirst(OpenIddictConstants.Claims.PostalCode)?.Value,
            [OpenIddictConstants.Claims.StreetAddress] = User.FindFirst(OpenIddictConstants.Claims.StreetAddress)?.Value,
            [OpenIddictConstants.Claims.Country] = User.FindFirst(OpenIddictConstants.Claims.Country)?.Value,
        };

        return new JsonResult(claims);
    }

    /// <summary>
    /// End session endpoint - handles /connect/logout
    /// </summary>
    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return SignOut(new AuthenticationProperties { RedirectUri = "/" }, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
