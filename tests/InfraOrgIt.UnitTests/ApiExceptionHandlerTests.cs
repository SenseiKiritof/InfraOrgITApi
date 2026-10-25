using InfraOrgIt.Api.BuildingBlocks.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.UnitTests;

public sealed class ApiExceptionHandlerTests
{
    [Fact]
    public async Task Foreign_key_violation_returns_409()
    {
        var context = CreateContext();
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance);
        var exception = new DbUpdateException(
            "delete",
            new Exception("Cannot delete or update a parent row: a foreign key constraint fails"));

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_entry_returns_409()
    {
        var context = CreateContext();
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance);
        var exception = new DbUpdateException(
            "insert",
            new Exception("Duplicate entry 'x' for key 'PRIMARY'"));

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    private static DefaultHttpContext CreateContext()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();

        return new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() }
        };
    }
}
