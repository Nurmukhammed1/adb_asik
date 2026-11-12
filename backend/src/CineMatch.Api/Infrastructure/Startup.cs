using CineMatch.Core;
using CineMatch.Infrastructure.Mongo;
using Microsoft.AspNetCore.Diagnostics;

namespace CineMatch.Api.Infrastructure;

/// <summary>Creates the collections and indexes before the API starts taking requests.</summary>
public class MongoInitializer(IndexInitializer indexes, ILogger<MongoInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await indexes.EnsureAsync(cancellationToken);
        logger.LogInformation("MongoDB collections and indexes are ready.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Turns a <see cref="ConflictException"/> into an HTTP 409 problem response.</summary>
public class ConflictExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ConflictException) return false;

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = StatusCodes.Status409Conflict, Title = exception.Message },
        });
    }
}
