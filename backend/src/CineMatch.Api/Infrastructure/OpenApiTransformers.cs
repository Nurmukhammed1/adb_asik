using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CineMatch.Api.Infrastructure;

/// <summary>Sets the document title and declares the login-token (JWT bearer) scheme.</summary>
internal sealed class ApiInfoTransformer : IOpenApiDocumentTransformer
{
    public const string BearerScheme = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "CineMatch API",
            Version = "v1",
            Description =
                "Movie streaming platform with personal recommendations, built on MongoDB and MovieLens data. " +
                "Get a token from `POST /api/auth/login` (or `/register`) and send it in the header " +
                "`Authorization: Bearer <token>`.",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "The token returned by POST /api/auth/login.",
        };
        return Task.CompletedTask;
    }
}

/// <summary>Shows query parameters the way clients write them: <c>pageSize</c>, not <c>PageSize</c>.</summary>
internal sealed class CamelCaseQueryTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            if (parameter.In == ParameterLocation.Query && parameter.Name is { Length: > 0 } name)
                parameter.Name = char.ToLowerInvariant(name[0]) + name[1..];
        }
        return Task.CompletedTask;
    }
}

/// <summary>Marks every endpoint that needs a login with the bearer scheme and its 401/403 responses.</summary>
internal sealed class BearerOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var authorize = metadata.OfType<IAuthorizeData>().ToList();
        if (authorize.Count == 0 || metadata.OfType<IAllowAnonymous>().Any()) return Task.CompletedTask;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(ApiInfoTransformer.BearerScheme, context.Document)] = [],
        });

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid login token." });
        if (authorize.Any(a => !string.IsNullOrEmpty(a.Roles)))
            operation.Responses.TryAdd("403", new OpenApiResponse { Description = "The account is not an admin." });
        return Task.CompletedTask;
    }
}
