using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MiniErp.Api;
using Xunit;

namespace MiniErp.Api.Tests;

public sealed class ApiProblemDetailsTests
{
    private static ProblemDetailsContext Context(ProblemDetails problem) => new()
    {
        HttpContext = new DefaultHttpContext { TraceIdentifier = "request-trace" },
        ProblemDetails = problem
    };

    [Fact]
    public void Validation_response_keeps_400_and_field_errors_when_extensions_are_initially_absent()
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["code"] = ["code cannot contain a null character."]
        }) { Status = StatusCodes.Status400BadRequest };

        ApiProblemDetails.Customize(Context(problem));

        Assert.Equal(400, problem.Status);
        Assert.Equal("code cannot contain a null character.", Assert.Single(problem.Errors["code"]));
        Assert.Equal("request_failed", problem.Extensions["code"]);
        Assert.Equal("request-trace", problem.Extensions["correlationId"]);
    }

    [Fact]
    public void Missing_extensions_receive_defaults_without_throwing_for_missing_dictionary_keys()
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status404NotFound };

        ApiProblemDetails.Customize(Context(problem));

        Assert.Equal("resource_not_found", problem.Extensions["code"]);
        Assert.Equal("request-trace", problem.Extensions["correlationId"]);
        Assert.Equal("Kaynak bulunamadı", problem.Title);
    }

    [Fact]
    public void Existing_non_null_extensions_and_problem_metadata_are_preserved()
    {
        var problem = new ProblemDetails { Status = 409, Title = "Known conflict", Type = "urn:mini-erp:conflict" };
        problem.Extensions["code"] = "duplicate_product_code";
        problem.Extensions["correlationId"] = "upstream-correlation";
        problem.Extensions["other"] = "kept";

        ApiProblemDetails.Customize(Context(problem));

        Assert.Equal(409, problem.Status);
        Assert.Equal("Known conflict", problem.Title);
        Assert.Equal("urn:mini-erp:conflict", problem.Type);
        Assert.Equal("duplicate_product_code", problem.Extensions["code"]);
        Assert.Equal("upstream-correlation", problem.Extensions["correlationId"]);
        Assert.Equal("kept", problem.Extensions["other"]);
    }

    [Fact]
    public void Explicit_null_extensions_receive_defaults()
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status400BadRequest };
        problem.Extensions["code"] = null;
        problem.Extensions["correlationId"] = null;

        ApiProblemDetails.Customize(Context(problem));

        Assert.Equal("request_failed", problem.Extensions["code"]);
        Assert.Equal("request-trace", problem.Extensions["correlationId"]);
    }

    [Fact]
    public void Missing_status_defaults_to_safe_500_problem_without_exception_details()
    {
        var problem = new ProblemDetails();

        ApiProblemDetails.Customize(Context(problem));

        Assert.Equal(500, problem.Status);
        Assert.Equal("https://httpstatuses.com/500", problem.Type);
        Assert.Equal("Beklenmeyen sunucu hatası", problem.Title);
        Assert.Equal("internal_error", problem.Extensions["code"]);
        Assert.Equal("request-trace", problem.Extensions["correlationId"]);
        Assert.Null(problem.Detail);
    }
}
