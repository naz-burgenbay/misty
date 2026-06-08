using Microsoft.JSInterop;

namespace Misty.Web.Services.Auth;

public interface ILocalStorage
{
    ValueTask<string?> GetAsync(string key);
    ValueTask SetAsync(string key, string value);
    ValueTask RemoveAsync(string key);
}

public sealed class JsLocalStorage : ILocalStorage
{
    private readonly IJSRuntime _js;
    public JsLocalStorage(IJSRuntime js) => _js = js;

    public ValueTask<string?> GetAsync(string key) =>
        _js.InvokeAsync<string?>("localStorage.getItem", key);

    public ValueTask SetAsync(string key, string value) =>
        _js.InvokeVoidAsync("localStorage.setItem", key, value);

    public ValueTask RemoveAsync(string key) =>
        _js.InvokeVoidAsync("localStorage.removeItem", key);
}
