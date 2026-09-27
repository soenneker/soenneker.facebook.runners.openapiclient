using Soenneker.Tests.HostedUnit;

namespace Soenneker.Facebook.Runners.OpenApiClient.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class FacebookOpenApiClientRunnerTests : HostedUnitTest
{
    public FacebookOpenApiClientRunnerTests(Host host) : base(host)
    {
    }

    [Test]
    public void Default()
    {

    }
}
