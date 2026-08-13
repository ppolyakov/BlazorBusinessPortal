using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    [Theory]
    [InlineData("POST", "/Account/Manage", true)]
    [InlineData("POST", "/Account/Manage/ChangePassword", true)]
    [InlineData("POST", "/account/avatar", true)]
    [InlineData("POST", "/account/avatar/remove", true)]
    [InlineData("GET", "/Account/Manage", false)]
    [InlineData("POST", "/Account/Logout", false)]
    [InlineData("POST", "/account/demo-login", false)]
    public void Demo_account_protection_targets_only_account_mutations(string method, string path, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        Assert.Equal(expected, BusinessPortal.Web.Services.DemoAccountProtection.IsBlockedRequest(context.Request));
    }

    [Fact]
    public async Task Public_demo_rejects_direct_account_mutation_requests()
    {
        await using var factory = new PortalWebFactory(fixture.ConnectionString, demoAccountLocked: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.PostAsync("/account/avatar/remove", content: null);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Account changes are disabled in the public demo.", await response.Content.ReadAsStringAsync());
    }

    private sealed class PortalWebFactory(string connectionString, bool demoAccountLocked = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["SeedDemoData"] = demoAccountLocked ? "true" : "false",
                    ["DemoAccess:Enabled"] = demoAccountLocked ? "true" : "false"
                }));
            if (demoAccountLocked)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                            options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                        })
                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                });
            }
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "DemoTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "demo-test-user")], SchemeName);
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
