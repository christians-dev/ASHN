using Microsoft.AspNetCore.Http;

namespace Ashen.BuildingBlocks.Web;

/// <summary>Turns expected failures into RFC 9457 problem details at the API edge.</summary>
public static class ResultHttpExtensions
{
    public static IResult ToProblem(this Error error) => error.Kind switch
    {
        ErrorKind.Validation => TypedResults.ValidationProblem(
            error.Fields?.ToDictionary(f => f.Key, f => f.Value) ?? new Dictionary<string, string[]>(),
            detail: error.Message,
            type: error.Code),
        ErrorKind.NotFound => Problem(StatusCodes.Status404NotFound, error),
        ErrorKind.Conflict => Problem(StatusCodes.Status409Conflict, error),
        ErrorKind.Forbidden => Problem(StatusCodes.Status403Forbidden, error),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error.Kind, "Unknown error kind."),
    };

    public static IResult ToHttp(this Result result, Func<IResult> onSuccess) =>
        result.IsSuccess ? onSuccess() : result.Error!.ToProblem();

    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error!.ToProblem();

    private static IResult Problem(int status, Error error) =>
        TypedResults.Problem(detail: error.Message, statusCode: status, type: error.Code);
}
