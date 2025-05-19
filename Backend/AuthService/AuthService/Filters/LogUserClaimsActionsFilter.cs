using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Claims;

namespace AuthService.Filters
{
    public class LogUserClaimsActionFilter : IActionFilter
    {
        private readonly ILogger<LogUserClaimsActionFilter> _logger;

        public LogUserClaimsActionFilter(ILogger<LogUserClaimsActionFilter> logger)
        {
            _logger = logger;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var user = context.HttpContext.User;
            if (user?.Identity?.IsAuthenticated ?? false)
            {
                var claimsInfo = string.Join("; ", user.Claims.Select(c => $"{c.Type}={c.Value}"));

                var roleClaim = user.FindFirstValue(ClaimTypes.Role);
            }
            else
            {
                _logger.LogInformation("--- User NOT Authenticated in Action Filter ({ActionName}) ---",
                     context.ActionDescriptor.DisplayName);
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}