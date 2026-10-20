using System.Text.Json;
using Microsoft.JSInterop;

namespace DwgMapOverlayPoc.Tests;

/// <summary>Records JS calls; handler returns raw JSON, an Exception to throw, or null.</summary>
internal sealed class FakeJSRuntime : IJSRuntime
{
    public List<(string Id, object?[] Args)> Calls { get; } = [];
    public Func<string, object?[], object?>? Handler { get; set; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        args ??= [];
        Calls.Add((identifier, args));
        var result = Handler?.Invoke(identifier, args);
        if (result is Exception ex) throw ex;
        if (result is string json && typeof(TValue) != typeof(IJSVoidResult))
            return new(JsonSerializer.Deserialize<TValue>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!);
        return new(default(TValue)!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => InvokeAsync<TValue>(identifier, args);

    public int Count(string id) => Calls.Count(c => c.Id == id);
}
