using Misty.Web.Services.Common;

namespace Misty.Web.Services.Ux;

public enum ToastKind { Info, Success, Warning, Error }

public sealed record ToastRequest(Guid Id, string Message, ToastKind Kind, TimeSpan? Duration);

// Imperative queue consumed by the global Toast host component in AppShell.
public interface IToastService
{
    Observable<IReadOnlyList<ToastRequest>> Toasts { get; }
    void Show(string message, ToastKind kind = ToastKind.Info, TimeSpan? duration = null);
    void Dismiss(Guid id);
}

public sealed class StubToastService : IToastService
{
    public Observable<IReadOnlyList<ToastRequest>> Toasts { get; } =
        new(Array.Empty<ToastRequest>());

    public void Show(string message, ToastKind kind = ToastKind.Info, TimeSpan? duration = null)
    {
        var next = new ToastRequest(Guid.NewGuid(), message, kind, duration ?? TimeSpan.FromSeconds(4));
        Toasts.Set(Toasts.Value.Append(next).ToList());
    }

    public void Dismiss(Guid id) =>
        Toasts.Set(Toasts.Value.Where(t => t.Id != id).ToList());
}
