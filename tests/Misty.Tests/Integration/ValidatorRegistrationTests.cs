using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Misty.Application.Users;
using System.Reflection;

namespace Misty.Tests.Integration;

[Collection("Integration")]
public sealed class ValidatorRegistrationTests
{
    private readonly IServiceProvider _services;

    public ValidatorRegistrationTests(ApiFactory factory)
    {
        _services = factory.Services;
    }

    [Fact]
    public void AllRequestsShouldHaveRegisteredValidators()
    {
        var applicationAssembly = typeof(RegisterUserCommand).Assembly;
        var requestTypes = applicationAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && IsRequest(t))
            .ToList();

        requestTypes.Should().NotBeEmpty("the application assembly should contain IRequest types");

        var missingValidators = new List<string>();

        using var scope = _services.CreateScope();
        var scopedServices = scope.ServiceProvider;

        foreach (var requestType in requestTypes)
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(requestType);
            var validator = scopedServices.GetService(validatorType);

            if (validator is null)
            {
                missingValidators.Add(requestType.Name);
            }
        }

        missingValidators.Should().BeEmpty(
            $"every IRequest type must have a registered validator. " +
            $"Missing validators for: {string.Join(", ", missingValidators)}");
    }

    private static bool IsRequest(Type type)
    {
        return type.GetInterfaces().Any(i =>
            i.IsGenericType &&
            (i.GetGenericTypeDefinition() == typeof(IRequest<>) ||
             i == typeof(IRequest)));
    }
}
