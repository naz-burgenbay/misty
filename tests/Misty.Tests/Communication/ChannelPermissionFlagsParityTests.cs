using FluentAssertions;
using Misty.Domain.Communication;
using Misty.Web.Services.Permissions;

namespace Misty.Tests.Communication;

// Guards against drift between the backend ChannelPermission enum and the frontend ChannelPermissionFlags mirror. The wire protocol casts the backend long directly into the frontend enum, so every backend name must exist on the frontend with the same numeric value.
public sealed class ChannelPermissionFlagsParityTests
{
    [Fact]
    public void EveryBackendName_ExistsOnFrontend_WithSameNumericValue()
    {
        var backend = Enum.GetValues<ChannelPermission>()
            .Where(v => v != ChannelPermission.All) // composite, not a single bit
            .ToDictionary(v => v.ToString(), v => (long)v);

        foreach (var (name, expected) in backend)
        {
            Enum.TryParse<ChannelPermissionFlags>(name, out var frontendValue)
                .Should().BeTrue($"frontend ChannelPermissionFlags is missing '{name}'");
            ((long)frontendValue).Should().Be(expected,
                $"frontend '{name}' must have the same numeric value as the backend");
        }
    }

    [Fact]
    public void EveryFrontendName_ExistsOnBackend_WithSameNumericValue()
    {
        var frontend = Enum.GetValues<ChannelPermissionFlags>()
            .ToDictionary(v => v.ToString(), v => (long)v);

        foreach (var (name, expected) in frontend)
        {
            Enum.TryParse<ChannelPermission>(name, out var backendValue)
                .Should().BeTrue($"backend ChannelPermission is missing '{name}'");
            ((long)backendValue).Should().Be(expected,
                $"backend '{name}' must have the same numeric value as the frontend");
        }
    }
}
