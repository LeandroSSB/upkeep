using System.Net;
using Xunit;

namespace Upkeep.IntegrationTests;

[Collection("ApiTests")]
public class DocsTests(ApiFixture fixture)
{
    [Fact]
    public async Task Openapi_v1_doc_retorna_200_e_expoe_endpoints()
    {
        var resp = await fixture.CreateClient().GetAsync("/openapi/v1.json",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"/assets\"", body);
        Assert.Contains("\"/auth/register\"", body);
    }

    [Fact]
    public async Task Scalar_ui_disponivel_fora_de_producao()
    {
        var resp = await fixture.CreateClient().GetAsync("/scalar",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/html", resp.Content.Headers.ContentType?.MediaType);
    }
}
