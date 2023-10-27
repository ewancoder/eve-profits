using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EveProfits.ConsoleApp;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Decorates a dependency wrapping an instance of Decorator around another
    /// class based off the same type. Decorator should accept its base
    /// interface into its constructor.
    /// Lifetime of the decorator instance will be the same as of the previously
    /// registered instance.
    /// </summary>
    public static IServiceCollection Decorate<TInterface, TDecorator>(this IServiceCollection services)
        where TDecorator : TInterface
    {
        var wrappedDescriptor = services.LastOrDefault(
            s => s.ServiceType == typeof(TInterface));

        if (wrappedDescriptor == null)
            throw new InvalidOperationException($"{typeof(TInterface).Name} is not registered.");

        var objectFactory = ActivatorUtilities.CreateFactory(
            typeof(TDecorator),
            new[] { typeof(TInterface) });

        services.Replace(ServiceDescriptor.Describe(
            typeof(TInterface),
            s => (TInterface)objectFactory(s, new[] { s.CreateInstance(wrappedDescriptor) }),
            wrappedDescriptor.Lifetime));

        return services;
    }

    /// <summary>
    /// Decorates a dependency wrapping an instance of Decorator around another
    /// class based off the same type. Decorator should accept its base
    /// interface into its constructor.
    /// Lifetime of the decorator instance will be as specified in the method.
    /// </summary>
    public static IServiceCollection Decorate<TInterface, TDecorator>(
        this IServiceCollection services,
        ServiceLifetime decoratorLifetime)
        where TDecorator : TInterface
    {
        var wrappedDescriptor = services.LastOrDefault(
            s => s.ServiceType == typeof(TInterface));

        if (wrappedDescriptor == null)
            throw new InvalidOperationException($"{typeof(TInterface).Name} is not registered.");

        var objectFactory = ActivatorUtilities.CreateFactory(
            typeof(TDecorator),
            new[] { typeof(TInterface) });

        services.Replace(ServiceDescriptor.Describe(
            typeof(TInterface),
            s => (TInterface)objectFactory(s, new[] { s.CreateInstance(wrappedDescriptor) }),
            decoratorLifetime));

        return services;
    }

    private static object CreateInstance(this IServiceProvider services, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance != null)
            return descriptor.ImplementationInstance;

        if (descriptor.ImplementationFactory != null)
            return descriptor.ImplementationFactory(services);

        if (descriptor.ImplementationType == null)
            throw new InvalidOperationException("Implementation type is null, cannot decorate.");

        return ActivatorUtilities.GetServiceOrCreateInstance(services, descriptor.ImplementationType);
    }
}
