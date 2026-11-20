using BsaTroopFinances.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BsaTroopFinances.Tests;

public class DevAuthTests
{
    private static IConfiguration Config(bool enabled) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["DevAuth:Enabled"] = enabled.ToString() })
        .Build();

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "BsaTroopFinances";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Refuses_to_start_when_enabled_outside_development(string environment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DevAuth.IsEnabled(Config(true), new Env(environment)));
        Assert.Contains("local development only", ex.Message);
    }

    [Fact]
    public void Enabled_only_in_development_with_the_setting_on()
    {
        Assert.True(DevAuth.IsEnabled(Config(true), new Env("Development")));
        Assert.False(DevAuth.IsEnabled(Config(false), new Env("Development")));
        Assert.False(DevAuth.IsEnabled(Config(false), new Env("Production")));
    }
}
