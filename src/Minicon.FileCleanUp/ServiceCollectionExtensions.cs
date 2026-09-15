using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.IO.Abstractions;
using System.Text.Json;

namespace Minicon.FileCleanUp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFileCleanUp(this IServiceCollection services, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return Register(
            services,
            () => CleanupConfigurationBinding.Read(section));
    }

    public static IServiceCollection AddFileCleanUp(this IServiceCollection services, CleanupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Register(
            services,
            () => JsonSerializer.Deserialize<CleanupOptions>(JsonSerializer.Serialize(options))!);
    }

    /// <summary>Reads unchanged legacy rule JSON from a host-owned provider at the start of each run.</summary>
    public static IServiceCollection AddFileCleanUp(
        this IServiceCollection services,
        IConfigurationSection section,
        Func<CancellationToken, Task<IEnumerable<string>>> readLegacyRuleJson)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(readLegacyRuleJson);
        services.TryAddSingleton<IFileSystem, FileSystem>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddTransient<IFileCleanUpService>(sp => new LegacyConfiguredService(
            section,
            readLegacyRuleJson,
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<FileCleanUpService>>()));
        return services;
    }

    private sealed class LegacyConfiguredService(
        IConfigurationSection section,
        Func<CancellationToken, Task<IEnumerable<string>>> readRules,
        IFileSystem fileSystem,
        TimeProvider time,
        ILogger<FileCleanUpService>? logger) : IFileCleanUpService
    {
        public async Task<CleanupResult> RunAsync(CancellationToken cancellationToken = default)
        {
            CleanupOptions options;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var json = await readRules(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                options = LegacyCleanupAdapter.Convert(CleanupConfigurationBinding.Read(section), json, fileSystem);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new CleanupResult { Status = CleanupStatus.Cancelled };
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
            {
                logger?.LogError(new EventId(3002), ex, "{EventType}: legacy configuration binding failed", "ConfigurationInvalid");
                return new CleanupResult { Status = CleanupStatus.InvalidConfiguration };
            }

            return await new FileCleanUpService(options, fileSystem, time, logger: logger).RunAsync(cancellationToken);
        }
    }

    private static IServiceCollection Register(IServiceCollection services, Func<CleanupOptions> read)
    {
        services.TryAddSingleton<IFileSystem, FileSystem>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddTransient<IFileCleanUpService>(sp => new ConfiguredService(
            read,
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<FileCleanUpService>>()));
        return services;
    }

    private sealed class ConfiguredService(
        Func<CleanupOptions> read,
        IFileSystem fs,
        TimeProvider time,
        ILogger<FileCleanUpService>? logger) : IFileCleanUpService
    {
        public async Task<CleanupResult> RunAsync(CancellationToken cancellationToken = default)
        {
            CleanupOptions options;
            try
            {
                options = read();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException)
            {
                logger?.LogError(new EventId(3002), ex, "{EventType}: configuration binding failed", "ConfigurationInvalid");
                return new CleanupResult
                {
                    Status = CleanupStatus.InvalidConfiguration
                };
            }

            return await new FileCleanUpService(options, fs, time, logger: logger).RunAsync(cancellationToken);
        }
    }
}
