using System.Text.Json;
using CampusTrack.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Middleware;

/// <summary>
/// Turns every unhandled exception into a consistent ProblemDetails response.
///
/// The rule this enforces is that internal detail never reaches a client. A parent seeing
/// "MySqlException: Duplicate entry for key ux_daily_student_date" learns nothing useful and
/// is told something about the schema; they get a sentence they can act on instead, while the
/// full exception goes to the log with a trace id they can quote to the school.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            // Too late to replace the response; make sure the failure is still recorded.
            _logger.LogError(exception, "Exception after the response had started for {Path}", context.Request.Path);
            return;
        }

        var traceId = context.TraceIdentifier;

        var (status, title, detail, code) = exception switch
        {
            ValidationException validation => (
                StatusCodes.Status400BadRequest,
                "Some details need correcting",
                string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)),
                "validation_failed"),

            DomainException domain => (
                StatusCodes.Status409Conflict,
                "That action could not be completed",
                domain.Message,
                domain.Code),

            UnauthorizedAccessException unauthorised => (
                StatusCodes.Status401Unauthorized,
                "Sign-in required",
                unauthorised.Message,
                "unauthorised"),

            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "Not found",
                "The item you asked for does not exist, or you do not have access to it.",
                "not_found"),

            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "Someone else changed this first",
                "This record was updated by another user while you were editing it. Reload and try again.",
                "concurrency_conflict"),

            DbUpdateException dbUpdate => (
                StatusCodes.Status409Conflict,
                "That change could not be saved",
                DescribeDbFailure(dbUpdate),
                "database_conflict"),

            OperationCanceledException => (
                StatusCodes.Status499ClientClosedRequest,
                "Request cancelled",
                "The request was cancelled before it finished.",
                "cancelled"),

            TimeoutException => (
                StatusCodes.Status504GatewayTimeout,
                "This is taking too long",
                "The operation timed out. Please try again in a moment.",
                "timeout"),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Something went wrong",
                "We could not complete that request. Please try again, and quote the reference below if it keeps happening.",
                "server_error")
        };

        // Client mistakes are warnings; anything the server got wrong is an error worth alerting on.
        if (status >= 500)
            _logger.LogError(exception, "Unhandled exception on {Method} {Path} (trace {TraceId})",
                context.Request.Method, context.Request.Path, traceId);
        else
            _logger.LogWarning("{Code} on {Method} {Path}: {Message}",
                code, context.Request.Method, context.Request.Path, exception.Message);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
            Type = $"https://campustrack.dev/errors/{code}"
        };

        problem.Extensions["traceId"] = traceId;
        problem.Extensions["code"] = code;

        if (exception is ValidationException validationEx)
        {
            problem.Extensions["errors"] = validationEx.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => ToCamelCase(g.Key), g => g.Select(e => e.ErrorMessage).ToArray());
        }

        // Stack traces are a development affordance only; in any other environment they are
        // an information leak.
        if (_environment.IsDevelopment())
        {
            problem.Extensions["exception"] = exception.GetType().Name;
            problem.Extensions["stackTrace"] = exception.StackTrace;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        }));
    }

    /// <summary>
    /// Translates the database constraint failures a user can actually cause into something
    /// they can act on, without echoing index names back to them.
    /// </summary>
    private static string DescribeDbFailure(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;

        if (message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase))
            return "A record with those details already exists.";

        if (message.Contains("foreign key constraint fails", StringComparison.OrdinalIgnoreCase))
            return "This item is still referenced elsewhere, so it cannot be changed or removed.";

        if (message.Contains("Data too long", StringComparison.OrdinalIgnoreCase))
            return "One of the values you entered is too long.";

        return "The change could not be saved. Please check the details and try again.";
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}

public static class StatusCodesExtensions
{
    /// <summary>Nginx's code for "client went away"; useful to distinguish from a real 500.</summary>
    public const int Status499ClientClosedRequest = 499;
}
