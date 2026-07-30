using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BusinessPortal.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class WebSecurityTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Protected_page_redirects_anonymous_user_to_login()
    {
        await using var factory = new PortalWebFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/clients");
        Assert.True(response.StatusCode is System.Net.HttpStatusCode.Redirect or System.Net.HttpStatusCode.Found);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PortalWebFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["SeedDemoData"] = "false"
                }));
        }
    }
}
