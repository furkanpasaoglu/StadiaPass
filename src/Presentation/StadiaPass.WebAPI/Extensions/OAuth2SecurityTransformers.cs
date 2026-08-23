using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using StadiaPass.WebAPI.Authorization;

namespace StadiaPass.WebAPI.Extensions;

/// <summary>Publishes the Keycloak authorization code flow so Scalar can render an Authorize button.</summary>
internal sealed class OAuth2SecuritySchemeTransformer(IOptions<KeycloakOptions> options)
    : IOpenApiDocumentTransformer
{
    public const string SchemeId = "OAuth2";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var keycloak = options.Value;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

        document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Description = $"Keycloak realm '{keycloak.Realm}'. Permissions are carried as realm roles and "
                          + "mapped onto policies at runtime.",
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = keycloak.AuthorizationUrl,
                    TokenUrl = keycloak.TokenUrl,
                    Scopes = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["openid"] = "Sign in",
                        ["profile"] = "Basic profile"
                    }
                }
            }
        };

        return Task.CompletedTask;
    }
}

/// <summary>
/// Marks every endpoint that carries an authorization policy as secured and documents which permission it
/// needs, so the required permission never has to be duplicated in a hand-written description.
/// </summary>
internal sealed class PermissionOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        string[] permissions =
        [
            .. context.Description.ActionDescriptor.EndpointMetadata
                .OfType<IAuthorizeData>()
                .Select(data => data.Policy)
                .Where(policy => !string.IsNullOrWhiteSpace(policy))
                .Select(policy => policy!)
                .Distinct(StringComparer.Ordinal)
        ];

        if (permissions.Length is 0)
        {
            return Task.CompletedTask;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(OAuth2SecuritySchemeTransformer.SchemeId)] = []
            }
        ];

        operation.Description = $"Requires permission: `{string.Join("`, `", permissions)}`";

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid token" });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Token lacks the required permission" });

        return Task.CompletedTask;
    }
}
