using BusinessPortal.Application;
using BusinessPortal.Infrastructure;
using BusinessPortal.Web.Components;
using BusinessPortal.Web.Components.Account;
using BusinessPortal.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpContextAccessor();
var dataProtectionKeysPath = builder.Configuration["DataProtectionKeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder.Services
        .AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}
else if (builder.Configuration.GetValue<bool>("EphemeralDataProtection"))
{
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
}
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(cookie =>
{
    cookie.Cookie.HttpOnly = true;
    cookie.Cookie.SameSite = SameSiteMode.Lax;
    cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
    cookie.SlidingExpiration = true;
});
builder.Services.AddAuthorization();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 10;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});
app.Use(async (context, next) =>
{
    // Public demo visitors may inspect Account features, but cannot mutate the shared demo identities.
    if (DemoAccountProtection.IsEnabled(app.Configuration)
        && DemoAccountProtection.IsBlockedRequest(context.Request))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync(DemoAccountProtection.Message);
        return;
    }

    await next();
});
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();
app.MapAvatarEndpoints();
app.MapPost("/account/demo-login", async (
    HttpContext context,
    IAntiforgery antiforgery,
    SignInManager<ApplicationUser> signInManager,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!configuration.GetValue<bool>("SeedDemoData")
        || !configuration.GetValue<bool>("DemoAccess:Enabled"))
    {
        return Results.NotFound();
    }

    try
    {
        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync(cancellationToken);
        var email = DemoLoginProfiles.ResolveEmail(form["profile"].ToString());
        var password = configuration["DemoPassword"];

        if (email is null || string.IsNullOrWhiteSpace(password))
        {
            return Results.BadRequest();
        }

        var result = await signInManager.PasswordSignInAsync(
            email,
            password,
            isPersistent: false,
            lockoutOnFailure: false);

        if (result.Succeeded)
        {
            return Results.LocalRedirect("/");
        }

        return Results.LocalRedirect("/Account/Login?demoError=unavailable");
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest();
    }
}).AllowAnonymous();
app.MapPost("/notifications/{id:long}/open", async (
    long id,
    HttpContext context,
    IAntiforgery antiforgery,
    INotificationService notifications,
    CancellationToken cancellationToken) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
        var targetUrl = await notifications.MarkReadAsync(id, cancellationToken);
        return Results.LocalRedirect(SafeLocalUrl(targetUrl));
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest();
    }
    catch (ResourceNotFoundException)
    {
        return Results.NotFound();
    }
}).RequireAuthorization();
app.MapPost("/notifications/read-all", async (
    HttpContext context,
    IAntiforgery antiforgery,
    INotificationService notifications,
    CancellationToken cancellationToken) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync(cancellationToken);
        await notifications.MarkAllReadAsync(cancellationToken);
        return Results.LocalRedirect(SafeLocalUrl(form["returnUrl"].ToString()));
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest();
    }
}).RequireAuthorization();
app.MapHealthChecks("/health");

if (args.Contains("--check-model", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var hasPendingChanges = db.Database.HasPendingModelChanges();
    Console.WriteLine(hasPendingChanges ? "The EF Core model has pending changes." : "The EF Core model matches the latest migration.");
    Environment.ExitCode = hasPendingChanges ? 1 : 0;
    return;
}

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    var healthPort = int.TryParse(builder.Configuration["PORT"], out var configuredPort)
        && configuredPort is > 0 and <= 65_535
            ? configuredPort
            : 8080;
    using var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    try
    {
        using var response = await healthClient.GetAsync($"http://localhost:{healthPort}/health");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        Environment.ExitCode = 1;
    }
    return;
}

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
    if (args.Contains("--reset-demo", StringComparer.Ordinal))
    {
        await seeder.ResetAndSeedAsync();
    }
    else if (args.Contains("--seed", StringComparer.Ordinal))
    {
        await seeder.SeedAsync();
    }
    return;
}

if (builder.Configuration.GetValue<bool>("DatabaseInitialization:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
}

app.Run();

static string SafeLocalUrl(string? targetUrl) =>
    !string.IsNullOrWhiteSpace(targetUrl)
    && targetUrl.StartsWith('/')
    && !targetUrl.StartsWith("//", StringComparison.Ordinal)
        ? targetUrl
        : "/";

public partial class Program;
