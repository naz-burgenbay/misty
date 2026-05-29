using Misty.Web.Services.Common;

namespace Misty.Web.Services.Ux;

public sealed record ConfirmRequest(
    string Title,
    string Message,
    string ConfirmLabel = "Confirm",
    string CancelLabel = "Cancel",
    bool IsDestructive = false);

// Imperative confirm/modal queue consumed by the global Modal host component in AppShell.
// ConfirmAsync resolves true if the user clicks confirm, false on cancel/dismiss.
public interface IModalService
{
    Observable<(ConfirmRequest Request, TaskCompletionSource<bool> Tcs)?> Current { get; }
    Task<bool> ConfirmAsync(ConfirmRequest request, CancellationToken ct = default);
    void Resolve(bool result);
}

public sealed class StubModalService : IModalService
{
    public Observable<(ConfirmRequest Request, TaskCompletionSource<bool> Tcs)?> Current { get; } = new(null);

    public Task<bool> ConfirmAsync(ConfirmRequest request, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Current.Set((request, tcs));
        if (ct.CanBeCanceled)
            ct.Register(() => { if (tcs.TrySetResult(false)) Current.Set(null); });
        return tcs.Task;
    }

    public void Resolve(bool result)
    {
        var current = Current.Value;
        if (current is null) return;
        Current.Set(null);
        current.Value.Tcs.TrySetResult(result);
    }
}
