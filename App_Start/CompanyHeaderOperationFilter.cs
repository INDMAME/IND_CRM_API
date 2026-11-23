using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http.Description;
using IND_CRM_API.Controllers;
using Swashbuckle.Swagger;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Agrega el header X-IND-Company en endpoints CRM para documentacion Swagger.
    /// </summary>
    public class CompanyHeaderOperationFilter : IOperationFilter
    {
        public void Apply(Operation operation, SchemaRegistry schemaRegistry, ApiDescription apiDescription)
        {
            if (operation == null || apiDescription == null)
                return;

            var controllerType = apiDescription.ActionDescriptor?.ControllerDescriptor?.ControllerType;
            if (controllerType == null || !typeof(BaseCrmController).IsAssignableFrom(controllerType))
                return;

            if (operation.parameters == null)
                operation.parameters = new List<Parameter>();

            var exists = operation.parameters.Any(p =>
                string.Equals(p.name, "X-IND-Company", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.@in, "header", StringComparison.OrdinalIgnoreCase));

            if (exists)
                return;

            operation.parameters.Add(new Parameter
            {
                name = "X-IND-Company",
                @in = "header",
                required = true,
                type = "string",
                description = "Compania AX requerida para endpoints CRM.",
                @default = "DAT"
            });
        }
    }
}
