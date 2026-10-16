using Altinn.Authorization.ProblemDetails;

namespace Shouldly;

[ShouldlyMethods]
public static class ResultShouldExtensions
{
    public static T ShouldHaveValue<T>(this Result<T> actual, string? customMessage = null)
        where T : notnull
    {
        actual.IsSuccess.ShouldBeTrue(customMessage);
        return actual.Value!;
    }

    public static ProblemInstance ShouldBeProblem<T>(this Result<T> actual, string? customMessage = null)
        where T : notnull
    {
        actual.IsProblem.ShouldBeTrue(customMessage);
        return actual.Problem!;
    }

    public static ProblemInstance ShouldBeProblem<T>(this Result<T> actual, ErrorCode errorCode, string? customMessage = null)
        where T : notnull
    {
        actual.IsProblem.ShouldBeTrue(customMessage);
        actual.Problem!.ErrorCode.ShouldBe(errorCode, customMessage);
        return actual.Problem!;
    }
}
