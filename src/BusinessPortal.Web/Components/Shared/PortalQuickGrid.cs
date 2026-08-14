using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;

namespace BusinessPortal.Web;

/// <summary>
/// Keeps a canceled JavaScript cleanup call from terminating a Blazor Server circuit.
/// </summary>
/// <typeparam name="TGridItem">The type represented by a grid row.</typeparam>
[CascadingTypeParameter(nameof(TGridItem))]
public sealed class PortalQuickGrid<TGridItem> : QuickGrid<TGridItem>, IAsyncDisposable
{
    public new async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        catch (OperationCanceledException)
        {
            // A circuit can be canceled between QuickGrid stopping its JavaScript listeners
            // and disposing their JS object references. The browser-side resources are already
            // unreachable at that point, so cancellation is an expected disposal outcome.
        }
    }
}
