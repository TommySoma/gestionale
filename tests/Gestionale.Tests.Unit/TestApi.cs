using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gestionale.Tests.Unit;

public sealed class TestApi(WebApplicationFactory<Program> fabbrica) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Il_controllo_di_salute_risponde()
    {
        using var client = fabbrica.CreateClient();

        var risposta = await client.GetAsync(new Uri("/salute", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Equal("Healthy", await risposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
