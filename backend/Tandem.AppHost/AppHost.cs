using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var authPostgres = builder.AddPostgres("auth-postgres")
    .WithPgAdmin()
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var authDb = authPostgres.AddDatabase("auth-db");

var auth = builder.AddProject<Projects.Authentication_API>("auth-api")
    .WithReference(authDb)
    .WaitFor(authDb);

//var scalar = builder.AddScalarApiReference(options => options
//    .PreferHttpsEndpoint()
//    .AllowSelfSignedCertificates()
//    .AddPreferredSecuritySchemes("OAuth2")
//    .AddOAuth2Authentication("OAuth2", oauth =>
//    {
//        oauth.WithFlows(flows =>
//        {
//            var authUrl = auth.GetEndpoint("https").Url;
//            var scalarUrl = scalar.GetEndpoint("http").Url;

//            flows.WithAuthorizationCode(flow =>
//            {
//                // Dynamically map the OIDC URLs using Aspire's dynamic reference string templates
//                flow
//                    .WithClientId("scalar-docs")
//                    .WithAuthorizationUrl($"{authUrl}/connect/authorize")
//                    .WithTokenUrl($"{authUrl}/connect/token")
//                    .WithRedirectUri($"{scalar.GetEndpoint("http").Url}")
//                    .WithPkce(Pkce.Sha256)
//                    .WithSelectedScopes("openid", "profile", "email", "roles", "tandem.api");
//            });
//        });
//    }));

var scalar = builder.AddScalarApiReference(options =>
{
    options
        .PreferHttpsEndpoint()
        .AllowSelfSignedCertificates()
        .AddPreferredSecuritySchemes("OAuth2");
});

scalar.WithApiReference(auth, (ScalarOptions options, CancellationToken cancellationToken) =>
{
    options.AddOAuth2Authentication("OAuth2", oauth =>
    {
        oauth.WithFlows(flows =>
        {
            flows.WithAuthorizationCode(flow =>
            {
                var authUrl = auth.GetEndpoint("https").Url;
                var scalarUrl = scalar.GetEndpoint("http").Url;

                flow
                    .WithClientId("scalar-docs")
                    .WithAuthorizationUrl($"{authUrl}/connect/authorize")
                    .WithTokenUrl($"{authUrl}/connect/token")
                    .WithRedirectUri($"{scalarUrl}/oauth2-redirect")
                    .WithPkce(Pkce.Sha256)
                    .WithSelectedScopes("openid", "profile", "email", "roles", "tandem.api");
            });
        });
    });

    return Task.CompletedTask;
});

auth.WithReference(scalar);

scalar
    .WithApiReference(auth)
    .WaitFor(auth);

builder.Build().Run();
