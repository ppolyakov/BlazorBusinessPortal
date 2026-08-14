namespace BusinessPortal.Web.Services;

public static class DemoLoginProfiles
{
    public static string? ResolveEmail(string? profile) => profile switch
    {
        "manager" => "manager@northstar.demo",
        "employee" => "employee@northstar.demo",
        _ => null
    };
}
