using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Reporting;

namespace PharmaX.Assistant;

public static class DependencyInjection
{
    /// <summary>
    /// The reports assistant. Registered whether or not a model is
    /// installed: without one it reports itself unavailable and the
    /// reports screen shows no Ask box.
    /// </summary>
    /// <remarks>
    /// Transient, so each reports screen gets its own and its scope
    /// disposes it - stopping the engine and freeing the model's memory -
    /// when the screen closes.
    /// </remarks>
    public static IServiceCollection AddPharmaXAssistant(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IReportSuggester, EngineReportSuggester>();

        // The same for the chat window open from every screen.
        services.AddTransient<Application.Assistant.IChatAssistant, EngineChatAssistant>();
        return services;
    }
}
