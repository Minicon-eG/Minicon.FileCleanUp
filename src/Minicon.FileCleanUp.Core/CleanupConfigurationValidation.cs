using System.IO.Abstractions;

namespace Minicon.FileCleanUp;

/// <summary>Allows a host to reject unsafe settings before creating its logs or other state.</summary>
public static class CleanupConfigurationValidation
{
    public static void Validate(this CleanupOptions options, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystem);
        try
        {
            ConfigurationValidator.Validate(options, fileSystem);
        }
        catch (NullReferenceException ex)
        {
            throw new ArgumentException("Configuration sections and collections must not be null.", nameof(options), ex);
        }
    }
}
