using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace StadiaPass.WebAPI.Endpoints;

public static class EndpointExtensions
{
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        ServiceDescriptor[] descriptors =
        [
            .. assembly.DefinedTypes
                .Where(type => type is { IsAbstract: false, IsInterface: false }
                               && type.IsAssignableTo(typeof(IEndpoint)))
                .Select(type => ServiceDescriptor.Transient(typeof(IEndpoint), type))
        ];

        services.TryAddEnumerable(descriptors);

        return services;
    }

    public static WebApplication MapEndpoints(this WebApplication app, RouteGroupBuilder? group = null)
    {
        IEndpointRouteBuilder builder = group is null ? app : group;

        foreach (var endpoint in app.Services.GetRequiredService<IEnumerable<IEndpoint>>())
        {
            endpoint.MapEndpoint(builder);
        }

        return app;
    }
}
