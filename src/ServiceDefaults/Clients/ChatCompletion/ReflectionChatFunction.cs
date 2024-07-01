using System.Text.Json;

namespace eShopSupport.ServiceDefaults.Clients.ChatCompletion;

internal static class ReflectionChatFunction
{
    public static async Task<object> InvokeAsync(Delegate @delegate, Dictionary<string, JsonElement> args)
    {
        // TODO: So much error handling
        var parameters = @delegate.Method.GetParameters();
        var argsInOrder = parameters.Select(p => args.ContainsKey(p.Name!) ? MapParameterType(p.ParameterType, args[p.Name!]) : null);
        var result = @delegate.DynamicInvoke(argsInOrder.ToArray());
        if (result is Task task)
        {
            await task;
            var resultProperty = task.GetType().GetProperty("Result");
            return resultProperty?.GetValue(task)!;
        }
        else
        {
            return Task.FromResult(result);
        }
    }

    private static object? MapParameterType(Type targetType, JsonElement receivedValue)
    {
        if (targetType == typeof(int) && receivedValue.ValueKind == JsonValueKind.Number)
        {
            return receivedValue.GetInt32();
        }
        else if (targetType == typeof(int?) && receivedValue.ValueKind == JsonValueKind.Number)
        {
            return receivedValue.GetInt32();
        }
        else if (Nullable.GetUnderlyingType(targetType) is not null && (receivedValue.ValueKind == JsonValueKind.Null || receivedValue.ValueKind == JsonValueKind.Undefined))
        {
            return null;
        }
        else if (targetType == typeof(string) && receivedValue.ValueKind == JsonValueKind.String)
        {
            return receivedValue.GetString();
        }

        throw new InvalidOperationException($"JSON value of kind {receivedValue.ValueKind} cannot be converted to {targetType}");
    }
}
