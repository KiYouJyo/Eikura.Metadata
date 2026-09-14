using Eizo.Metadata.Providers;

namespace Eizo.Metadata.Providers.Tests;

public sealed class RuntimeDiagnosticsTests
{
    [Fact]
    public void Probe_ReportsCoreAndProvidersFromOneRuntimeVersion()
    {
        var probe = MetadataRuntimeDiagnostics.Probe();

        Assert.True(probe.IsConsistent);
        Assert.Equal("0.2.21", probe.Version);
        Assert.Collection(
            probe.Modules.OrderBy(static module => module.AssemblyName),
            core =>
            {
                Assert.Equal("Eizo.Metadata.Core", core.AssemblyName);
                Assert.Equal("0.2.21", core.Version);
                Assert.False(string.IsNullOrWhiteSpace(core.AssemblyPath));
            },
            providers =>
            {
                Assert.Equal("Eizo.Metadata.Providers", providers.AssemblyName);
                Assert.Equal("0.2.21", providers.Version);
                Assert.False(string.IsNullOrWhiteSpace(providers.AssemblyPath));
            });
    }
}
