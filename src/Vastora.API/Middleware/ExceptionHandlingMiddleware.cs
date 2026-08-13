using System.Net;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common.Exceptions;

namespace Vastora.API.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var (status, title, errors) = ex switch
        {
            ValidationAppException v => (HttpStatusCode.BadRequest, v.Message, (IDictionary<string, string[]>)v.Errors),
            ValidationException fv => (HttpStatusCode.BadRequest, "One or more validation errors occurred.", ToErrorDictionary(fv)),
            NotFoundException nf => (HttpStatusCode.NotFound, nf.Message, null),
            ConflictException c => (HttpStatusCode.Conflict, c.Message, null),
            ForbiddenException f => (HttpStatusCode.Forbidden, f.Message, null),
            UnauthorizedAppException u => (HttpStatusCode.Unauthorized, u.Message, null),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.", null)
        };

        if (status == HttpStatusCode.InternalServerError)
        {
            logger.LogError(ex, "Unhandled exception while processing {Method} {Path}", context.Request.Method, context.Request.Path);
        }

        var problemDetails = new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Type = $"https://httpstatuses.io/{(int)status}"
        };

        if (errors is not null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsJsonAsync(problemDetails);
    }

    private static Dictionary<string, string[]> ToErrorDictionary(ValidationException ex) =>
        ex.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
}
