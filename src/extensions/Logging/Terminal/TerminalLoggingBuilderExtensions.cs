using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Configuration;

namespace Microsoft.Extensions.Logging.Terminal;

public static class TerminalLoggingBuilderExtensions
{
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026")]
    public static ILoggingBuilder AddTerminal(
        this ILoggingBuilder builder,
        Action<TerminalLoggerOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddConfiguration();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, TerminalLoggerProvider>());
        LoggerProviderOptions.RegisterProviderOptions<TerminalLoggerOptions, TerminalLoggerProvider>(builder.Services);

        if (configureOptions != null)
            _ = builder.Services.Configure(configureOptions);

        return builder;
    }
}
