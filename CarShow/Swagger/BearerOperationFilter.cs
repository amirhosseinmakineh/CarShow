using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CarShow.Swagger;

public sealed class BearerOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.MethodInfo.IsDefined(typeof(AuthorizeAttribute), true) &&
            !(context.MethodInfo.DeclaringType?.IsDefined(typeof(AuthorizeAttribute), true) ?? false)) return;
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
        });
    }
}
