using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Minicon.FileCleanUp;

internal static class CleanupConfigurationBinding
{
    internal static CleanupOptions Read(IConfigurationSection section)
    {
        var values = section.AsEnumerable(makePathsRelative: true)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        MapLegacyName(values, "DelayBetweenDelete", "DeleteDelayMilliseconds");
        MapLegacyName(values, "MinimumAgeInDays", "MinimumRetentionDays");

        var normalized = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        using var lifetime = normalized as IDisposable;

        return normalized.Get<CleanupOptions>(options => options.ErrorOnUnknownConfiguration = true) ?? new();
    }

    private static void MapLegacyName(
        Dictionary<string, string?> values,
        string legacyName,
        string currentName)
    {
        if (!values.Remove(legacyName, out var legacyValue))
        {
            return;
        }

        if (!int.TryParse(legacyValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyNumber))
        {
            throw new InvalidOperationException($"{legacyName} must be an integer.");
        }

        if (values.TryGetValue(currentName, out var currentValue))
        {
            if (!int.TryParse(currentValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentNumber)
                || currentNumber != legacyNumber)
            {
                throw new InvalidOperationException($"{legacyName} and {currentName} specify conflicting values.");
            }
        }
        else
        {
            values[currentName] = legacyValue;
        }
    }
}
