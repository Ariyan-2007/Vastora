using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Vastora.API.Filters;

/// <summary>
/// Runs the registered FluentValidation <see cref="IValidator{T}"/> (if any is registered)
/// against every action argument before the action executes. Failures are thrown as a
/// FluentValidation.ValidationException, which ExceptionHandlingMiddleware already maps to a
/// 400 with a per-field error dictionary — this filter is the missing wire-up, not new mapping.
/// </summary>
public class ValidationActionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        await next();
    }
}
