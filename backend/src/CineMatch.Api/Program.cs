using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CineMatch.Api.Auth;
using CineMatch.Api.Infrastructure;
using CineMatch.Infrastructure;
using CineMatch.Infrastructure.Mongo;
using CineMatch.Recommender;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// MongoDB, repositories, password hashing.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<MongoInitializer>();

// Recommendations: the online scoring, the offline similarity build, and its startup/nightly schedule.
builder.Services.AddOptions<RecommenderOptions>().Bind(builder.Configuration.GetSection("Recommender"));
builder.Services.AddSingleton(services => services.GetRequiredService<IOptions<RecommenderOptions>>().Value);
builder.Services.AddSingleton<RecommendationService>();
builder.Services.AddSingleton<SimilarityRebuilder>();
builder.Services.AddHostedService<RecommenderScheduler>();

// Login tokens. The signing key lives in User Secrets, so the app refuses to start without it.
builder.Services.AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .Validate(
        jwt => Encoding.UTF8.GetByteCount(jwt.SigningKey) >= 32,
        "Jwt:SigningKey is missing or shorter than 32 bytes. Set it with: " +
        "dotnet user-secrets set \"Jwt:SigningKey\" \"<long random string>\" --project src/CineMatch.Api")
    .ValidateOnStart();
builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, jwt) =>
    {
        options.MapInboundClaims = false; // keep the short claim names: sub, name, role
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = jwt.Value.CreateKey(),
            ValidateIssuerSigningKey = true,
            NameClaimType = JwtClaims.Name,
            RoleClaimType = JwtClaims.Role,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

// Enums travel as names ("rating"), numbers only as JSON numbers. The same rules are given to the
// OpenAPI generator, which reads the minimal-API JSON options, so the docs match the real behavior.
builder.Services.AddControllers().AddJsonOptions(json => ConfigureJson(json.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(json => ConfigureJson(json.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ConflictExceptionHandler>();
builder.Services.AddOpenApi(openApi =>
{
    openApi.AddDocumentTransformer<ApiInfoTransformer>();
    openApi.AddOperationTransformer<BearerOperationTransformer>();
    openApi.AddOperationTransformer<CamelCaseQueryTransformer>();
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages(); // error responses without a body become problem+json

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(scalar => scalar.WithTitle("CineMatch API"));   // docs UI at /scalar/v1
}
else
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", async (MongoContext mongo, CancellationToken ct) =>
{
    await mongo.PingAsync(ct);
    return Results.Ok(new { status = "ok" });
}).WithSummary("Checks that the API is up and can reach MongoDB.").WithTags("Health");

app.Run();

static void ConfigureJson(JsonSerializerOptions options)
{
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    options.NumberHandling = JsonNumberHandling.Strict;
}
