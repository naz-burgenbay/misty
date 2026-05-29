namespace Misty.Web.Services.Common;

// Observable wrapper used by the stub services. Phase 5 swaps the implementation to one driven by SignalR/HTTP responses, but the Subscribe/Value surface stays the same.
public sealed class Observable<T>
{
    private T _value;
    private event Action<T>? _changed;

    public Observable(T initial) => _value = initial;

    public T Value => _value;

    public void Set(T next)
    {
        _value = next;
        _changed?.Invoke(next);
    }

    public IDisposable Subscribe(Action<T> handler)
    {
        _changed += handler;
        handler(_value);
        return new Sub(() => _changed -= handler);
    }

    private sealed class Sub : IDisposable
    {
        private Action? _dispose;
        public Sub(Action dispose) => _dispose = dispose;
        public void Dispose() { _dispose?.Invoke(); _dispose = null; }
    }
}
