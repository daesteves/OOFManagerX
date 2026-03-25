using Microsoft.Extensions.DependencyInjection;
using OOFManagerX.Core.Interfaces;
using OOFManagerX.Core.Services;

namespace OOFManagerX.Core;

/// <summary>
/// Extension methods for registering OOFManagerX.Core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds OOFManagerX core services to the dependency injection container.
    /// </summary>
    public static IServiceCollection AddOOFManagerXCore(this IServiceCollection services)
    {
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IScheduleService, ScheduleService>();
        services.AddHttpClient<IOOFService, OOFService>();
        
        return services;
    }
}
