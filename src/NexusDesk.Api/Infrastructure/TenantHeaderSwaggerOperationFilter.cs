using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NexusDesk.Api.Infrastructure;

public sealed class TenantHeaderSwaggerOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= new List<OpenApiParameter>();

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Tenant-Subdomain",
            In = ParameterLocation.Header,
            Description = "Tenant subdomain slug (e.g., acme, beta, globex)",
            Required = false,
            Schema = new OpenApiSchema { Type = "string" }
        });

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Tenant-Id",
            In = ParameterLocation.Header,
            Description = "Optional Tenant GUID override",
            Required = false,
            Schema = new OpenApiSchema { Type = "string" }
        });

        // Detect IFormFile parameters for multipart/form-data upload in Minimal APIs
        var formFileParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (context.MethodInfo != null)
        {
            foreach (var param in context.MethodInfo.GetParameters())
            {
                if (typeof(IFormFile).IsAssignableFrom(param.ParameterType) ||
                    typeof(IFormFileCollection).IsAssignableFrom(param.ParameterType) ||
                    typeof(IEnumerable<IFormFile>).IsAssignableFrom(param.ParameterType))
                {
                    formFileParamNames.Add(param.Name ?? "file");
                }
            }
        }

        foreach (var desc in context.ApiDescription.ParameterDescriptions)
        {
            if (desc.Type != null && (typeof(IFormFile).IsAssignableFrom(desc.Type) ||
                typeof(IFormFileCollection).IsAssignableFrom(desc.Type) ||
                typeof(IEnumerable<IFormFile>).IsAssignableFrom(desc.Type)))
            {
                formFileParamNames.Add(desc.Name);
            }
        }

        var isAttachmentUpload = (context.ApiDescription.RelativePath != null &&
            context.ApiDescription.RelativePath.EndsWith("attachments", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(context.ApiDescription.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase));

        if (isAttachmentUpload && formFileParamNames.Count == 0)
        {
            formFileParamNames.Add("file");
        }

        if (formFileParamNames.Count > 0)
        {
            for (var i = operation.Parameters.Count - 1; i >= 0; i--)
            {
                if (formFileParamNames.Contains(operation.Parameters[i].Name))
                {
                    operation.Parameters.RemoveAt(i);
                }
            }

            var properties = new Dictionary<string, OpenApiSchema>();
            var requiredProps = new HashSet<string>();

            foreach (var name in formFileParamNames)
            {
                properties[name] = new OpenApiSchema
                {
                    Type = "string",
                    Format = "binary",
                    Description = "File to upload"
                };
                requiredProps.Add(name);
            }

            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["multipart/form-data"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = properties,
                            Required = requiredProps
                        }
                    }
                }
            };
        }
    }
}
