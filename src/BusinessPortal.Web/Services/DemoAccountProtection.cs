namespace BusinessPortal.Web.Services;

public static class DemoAccountProtection
{
    public const string Message = "Account changes are disabled in the public demo.";

    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool>("SeedDemoData")
        && configuration.GetValue<bool>("DemoAccess:Enabled");

    public static bool IsBlockedRequest(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method))
        {
            return false;
        }

        return request.Path.StartsWithSegments("/Account/Manage", StringComparison.OrdinalIgnoreCase)
            || request.Path.StartsWithSegments("/account/avatar", StringComparison.OrdinalIgnoreCase);
    }
}
