using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Replay;
using CrushRoyale.Server.Persistence;
using Microsoft.AspNetCore.Diagnostics;

namespace CrushRoyale.Server.Infrastructure;

/// <summary>A business error that must reach the client with a stable code.</summary>
public sealed class ApiException : Exception
{
    public ApiException(ErrorCode code, string? message = null) : base(message ?? code.ToString())
    {
        Code = code;
    }

    public ErrorCode Code { get; }
}

public static class ApiErrors
{
    public static int StatusFor(ErrorCode code) => code switch
    {
        ErrorCode.NotFound or ErrorCode.NotMember => StatusCodes.Status404NotFound,
        ErrorCode.PermissionDenied or ErrorCode.FeatureLocked or ErrorCode.StageLocked or ErrorCode.PowerUpLocked or ErrorCode.Banned => StatusCodes.Status403Forbidden,
        ErrorCode.DuplicateRequest or ErrorCode.AlreadyClaimed or ErrorCode.AlreadyMember or ErrorCode.NameTaken or ErrorCode.GuildFull or ErrorCode.SessionOver
            or ErrorCode.NotEnoughCoins or ErrorCode.NotEnoughOrbes or ErrorCode.NotEnoughLives or ErrorCode.NotEnoughItems or ErrorCode.LimitReached => StatusCodes.Status409Conflict,
        ErrorCode.CooldownActive => StatusCodes.Status429TooManyRequests,
        ErrorCode.VersionMismatch => StatusCodes.Status426UpgradeRequired,
        ErrorCode.ReplayInvalid or ErrorCode.ReplayMismatch => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status400BadRequest
    };

    public static void ThrowIfFailed(this OperationResult result)
    {
        if (!result.Success)
        {
            throw new ApiException(result.Error, result.Message);
        }
    }

    public static T ValueOrThrow<T>(this OperationResult<T> result)
    {
        if (!result.Success)
        {
            throw new ApiException(result.Error, result.Message);
        }
        return result.Value;
    }

    public static void ThrowIf(this ErrorCode code)
    {
        if (code != ErrorCode.None)
        {
            throw new ApiException(code);
        }
    }

    public static IResult ToResult(ErrorCode code, string? message) =>
        Results.Json(new ApiErrorDto { Code = code.ToString(), Message = message ?? code.ToString() }, Json.Options, statusCode: StatusFor(code));

    /// <summary>Global exception handler: business errors keep their code, everything else is logged and hidden.</summary>
    public static void UseApiErrorHandling(this WebApplication app)
    {
        app.UseExceptionHandler(builder => builder.Run(async context =>
        {
            Exception? error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            IResult result = error switch
            {
                ApiException api => ToResult(api.Code, api.Message),
                ReplayFormatException replay => ToResult(ErrorCode.ReplayInvalid, replay.Message),
                ConcurrencyException => Results.Json(new ApiErrorDto { Code = "Conflict", Message = "Please retry." }, Json.Options, statusCode: StatusCodes.Status409Conflict),
                BadHttpRequestException bad => ToResult(ErrorCode.InvalidArgument, bad.Message),
                System.Text.Json.JsonException json => ToResult(ErrorCode.InvalidArgument, "Malformed JSON: " + json.Message),
                _ => Results.Json(new ApiErrorDto { Code = "Internal", Message = "Unexpected server error." }, Json.Options, statusCode: StatusCodes.Status500InternalServerError)
            };

            if (result is IStatusCodeHttpResult { StatusCode: >= 500 })
            {
                app.Logger.LogError(error, "Unhandled error on {Path}", context.Request.Path);
            }
            await result.ExecuteAsync(context);
        }));
    }
}
