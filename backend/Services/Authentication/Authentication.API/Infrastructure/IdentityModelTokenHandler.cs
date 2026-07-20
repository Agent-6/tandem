using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;
using System.Security.Claims;
using System.Collections.Immutable;

namespace Authentication.API.Infrastructure;

/// <summary>
/// Delegating handler that creates a client credentials token via OpenIddict's dispatcher
/// and attaches it as a Bearer token to outgoing requests.
/// Tokens are cached to avoid repeated token creation.
/// </summary>
public class IdentityModelTokenHandler : DelegatingHandler
{
    private const string ClientId = "tandem-backend";
    private const string ClientSecret = "BACKEND_SECRET";
    private const string ApiScope = "tandem.api";

    private readonly IMemoryCache _cache;
    private readonly IOpenIddictServerDispatcher _dispatcher;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IOptions<OpenIddictServerOptions> _options;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<IdentityModelTokenHandler> _logger;

    public IdentityModelTokenHandler(
        IMemoryCache cache,
        IOpenIddictServerDispatcher dispatcher,
        IOpenIddictScopeManager scopeManager,
        IOptions<OpenIddictServerOptions> options,
        IHttpContextAccessor httpContextAccessor,
        ILogger<IdentityModelTokenHandler> logger)
    {
        _cache = cache;
        _dispatcher = dispatcher;
        _scopeManager = scopeManager;
        _options = options;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await GetCachedAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetCachedAccessTokenAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "client_credentials_token";

        // Try to get cached token
        if (_cache.TryGetValue(cacheKey, out CachedToken? cachedToken)
            && cachedToken != null
            && cachedToken.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) // 1 min buffer
        {
            return cachedToken.AccessToken;
        }

        // Create a new token
        var result = await CreateClientCredentialsTokenAsync(cancellationToken);

        // Cache it using the token's own expiry
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn - 30);
        _cache.Set(cacheKey, new CachedToken(result.AccessToken, expiresAt), expiresAt);

        return result.AccessToken;
    }

    private async Task<TokenResult> CreateClientCredentialsTokenAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating client credentials token via OpenIddict dispatcher");

        var transaction = new OpenIddictServerTransaction
        {
            Options = _options.Value,
            Request = new OpenIddictRequest
            {
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                Scope = ApiScope,
                GrantType = OpenIddictConstants.GrantTypes.ClientCredentials
            },
            Logger = _logger
        };

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType);
        identity.SetClaim(OpenIddictConstants.Claims.Subject, ClientId);
        identity.SetScopes(new[] { ApiScope }.ToImmutableArray());
        identity.SetClaim(OpenIddictConstants.Claims.Private.Issuer, GetIssuer());

        var resources = await _scopeManager
            .ListResourcesAsync(new[] { ApiScope }.ToImmutableArray(), cancellationToken)
            .ToListAsync(cancellationToken);
        identity.SetAudiences(resources);
        identity.SetCreationDate(DateTimeOffset.UtcNow);
        identity.SetExpirationDate(DateTimeOffset.UtcNow.AddMinutes(15));

        var notification = new GenerateTokenContext(transaction)
        {
            ClientId = ClientId,
            CreateTokenEntry = !_options.Value.DisableTokenStorage,
            IsReferenceToken = _options.Value.UseReferenceAccessTokens,
            PersistTokenPayload = _options.Value.UseReferenceAccessTokens,
            Principal = new ClaimsPrincipal(identity),
            TokenFormat = OpenIddictConstants.TokenFormats.Private.JsonWebToken,
            TokenType = OpenIddictConstants.TokenTypeIdentifiers.AccessToken
        };

        await _dispatcher.DispatchAsync(notification);

        var token = notification.Token
            ?? throw new InvalidOperationException("Error making access token");

        _logger.LogInformation("Successfully created client credentials token");

        return new TokenResult(token, 900);
    }

    private string GetIssuer()
    {
        if (_options.Value.Issuer != null)
        {
            return _options.Value.Issuer.AbsoluteUri;
        }

        ArgumentNullException.ThrowIfNull(_httpContextAccessor.HttpContext);
        var request = _httpContextAccessor.HttpContext.Request;
        return string.Concat(request.Scheme, "://", request.Host, request.PathBase);
    }

    private class CachedToken(string accessToken, DateTimeOffset expiresAt)
    {
        public string AccessToken { get; } = accessToken;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
    }

    private readonly record struct TokenResult(string AccessToken, int ExpiresIn);
}
