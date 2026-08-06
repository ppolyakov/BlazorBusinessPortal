namespace BusinessPortal.Web.Components;

public sealed record PortalSelectOption<TValue>(TValue Value, string Label, string? Description = null);
