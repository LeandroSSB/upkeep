using System.Net;
using Xunit;

namespace Upkeep.IntegrationTests;

[Collection("ApiTests")]
public class HealthTests(ApiFixture fixture)
{
    [Fact]
    public async Task Health_retorna_ok()
    {
        var resp = await fixture.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task HealthReady_conecta_no_banco()
    {
        var resp = await fixture.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
