using System;
using FluentAssertions;
using Memoria.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Memoria.Tests.App;

public class AppServicesTests
{
    [Fact]
    public void Resolve_returns_service_from_initialized_provider()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton("hello");
        var provider = sc.BuildServiceProvider();

        AppServices.Initialize(provider);

        AppServices.Provider.Should().BeSameAs(provider);
        AppServices.Resolve<string>().Should().Be("hello");
    }
}
