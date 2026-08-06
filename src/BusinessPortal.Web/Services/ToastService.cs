namespace BusinessPortal.Web.Services;

public enum ToastKind
{
    Success,
    Info,
    Warning,
    Error
}

public sealed record ToastMessage(
    Guid Id,
    ToastKind Kind,
    string Title,
    string? Message,
    string? ActionUrl,
    string? ActionLabel,
    string? PersonName,
    string? PersonAvatarUrl,
    TimeSpan Duration);

public sealed class ToastService
{
    public event Action<ToastMessage>? Requested;

    public void Success(string title, string? message = null) =>
        Show(ToastKind.Success, title, message);

    public void Info(
        string title,
        string? message = null,
        string? actionUrl = null,
        string? actionLabel = null,
        string? personName = null,
        string? personAvatarUrl = null) =>
        Show(ToastKind.Info, title, message, actionUrl, actionLabel, personName, personAvatarUrl, TimeSpan.FromSeconds(8));

    public void Warning(string title, string? message = null) =>
        Show(ToastKind.Warning, title, message, duration: TimeSpan.FromSeconds(8));

    public void Error(string title, string? message = null) =>
        Show(ToastKind.Error, title, message, duration: TimeSpan.FromSeconds(10));

    private void Show(
        ToastKind kind,
        string title,
        string? message,
        string? actionUrl = null,
        string? actionLabel = null,
        string? personName = null,
        string? personAvatarUrl = null,
        TimeSpan? duration = null) =>
        Requested?.Invoke(new(
            Guid.NewGuid(),
            kind,
            title,
            message,
            actionUrl,
            actionLabel,
            personName,
            personAvatarUrl,
            duration ?? TimeSpan.FromSeconds(5)));
}
