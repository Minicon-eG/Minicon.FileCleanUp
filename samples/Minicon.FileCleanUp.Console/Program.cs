using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minicon.FileCleanUp;
using Serilog;

if (args.Contains("--help"))
{
    System.Console.WriteLine("Configure FileCleanUp in appsettings.json or your configuration provider. DryRun defaults to true.\nOverrides: --FileCleanUp:DryRun true\nAudit inspection: --audit-list\nAcknowledge: --audit-ack <operation-guid> --operator <name> --reason <text>");
    return 0;
}
var recovery = args.Contains("--audit-list") || args.Contains("--audit-ack");
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = recovery ? [] : args,
    ContentRootPath = AppContext.BaseDirectory
});
// Register the customer's configuration provider here, before AddFileCleanUp.
var stateDirectory = Path.GetFullPath(builder.Configuration["CleanupHost:StateDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "state"));
Directory.CreateDirectory(stateDirectory);
var logConfiguration = new LoggerConfiguration().MinimumLevel.Information().Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Minicon.FileCleanUp.Console")
    .Enrich.WithProperty("MachineName", Environment.MachineName)
    .WriteTo.Console();
if (builder.Configuration["Seq:ServerUrl"] is { Length: > 0 } seq)
    logConfiguration.WriteTo.Seq(seq, apiKey: builder.Configuration["Seq:ApiKey"], bufferBaseFilename: Path.Combine(stateDirectory, "seq-buffer"));
Log.Logger = logConfiguration.CreateLogger();
builder.Services.AddSerilog(Log.Logger, dispose: false);
builder.Services.AddFileCleanUp(builder.Configuration.GetSection("FileCleanUp"));
try
{
    FileStream applicationLock;
    try { applicationLock = new FileStream(Path.Combine(stateDirectory, "cleanup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    catch (IOException ex) { Log.Warning(ex, "{EventType}: state directory lock unavailable", "CleanupNotStarted"); return 3; }
    using (applicationLock)
    using (var host = builder.Build())
    {
        if (recovery)
        {
            var journalPath = builder.Configuration["FileCleanUp:Audit:JournalDirectory"] ?? throw new ArgumentException("Audit.JournalDirectory required.");
            using var journal = new LocalAuditJournal(journalPath, new FileSystem());
            if (args.Contains("--audit-list")) System.Console.WriteLine(JsonSerializer.Serialize(journal.GetPending(), new JsonSerializerOptions { WriteIndented = true }));
            else
            {
                string Value(string key) { var index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"Missing {key}"); }
                journal.Acknowledge(Guid.Parse(Value("--audit-ack")), Value("--operator"), Value("--reason"));
                Log.Information("{EventType}: acknowledgement persisted", "AuditRecoveryRecorded");
            }
            return 0;
        }
        await host.StartAsync();
        try
        {
            var token = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
            var result = await host.Services.GetRequiredService<IFileCleanUpService>().RunAsync(token);
            await File.WriteAllTextAsync(Path.Combine(stateDirectory, $"{result.RunId}.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return result.Status switch { CleanupStatus.Succeeded => 0, CleanupStatus.InvalidConfiguration => 2, CleanupStatus.Limited => 4, CleanupStatus.Cancelled => 5, _ => 1 };
        }
        finally { await host.StopAsync(); }
    }
}
catch (Exception ex) { Log.Error(ex, "{EventType}: host failed", "CleanupHostFailed"); return 1; }
finally { await Log.CloseAndFlushAsync(); }
