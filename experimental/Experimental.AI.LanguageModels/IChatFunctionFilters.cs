namespace Experimental.AI.LanguageModels;

// If we want a standard way to attach filters, we can define this interface and have the IChatService implementation
// do this if they want to participate.
public interface IChatFunctionFilters
{
    void OnFunctionInvocation(IChatFunctionFilter filter);
}

public interface IChatFunctionFilter
{
    Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next);

    class FunctionInvocationContext { }
}
