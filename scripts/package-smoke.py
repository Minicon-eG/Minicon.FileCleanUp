"""Tests the public NuGet boundary: real package, fresh host, build and publish."""
from pathlib import Path
import subprocess, tempfile, zipfile, os, html
root = Path(__file__).resolve().parents[1]
out = root / 'artifacts' / 'packages'
env = os.environ.copy()
def run(*args, cwd=root):
    subprocess.run(args, cwd=cwd, env=env, check=True, stdout=subprocess.DEVNULL)
run('dotnet', 'pack', 'src/Minicon.FileCleanUp', '-c', 'Release', '-o', str(out))
package = max(out.glob('*.nupkg'), key=lambda p:p.stat().st_mtime)
with zipfile.ZipFile(package) as z:
    for assembly in ['Minicon.FileCleanUp', 'Minicon.FileCleanUp.Domain', 'Minicon.FileCleanUp.Core', 'Minicon.FileCleanUp.Infrastructure']:
        assert f'lib/net10.0/{assembly}.dll' in z.namelist(), f'{assembly} missing from the single package'
    assert 'docs/index.html' in z.namelist(), 'Offline HTML missing from package'
    assert 'build/Minicon.FileCleanUp.targets' in z.namelist(), 'Copy target missing'
    import xml.etree.ElementTree as ET
    spec=ET.fromstring(z.read(next(n for n in z.namelist() if n.endswith('.nuspec'))))
    assert not any(e.attrib.get('id', '').startswith('Minicon.FileCleanUp') for e in spec.iter()), 'Internal projects must not become separate package dependencies'
    version=next(e.text for e in spec.iter() if e.tag.endswith('}version') or e.tag=='version')
with tempfile.TemporaryDirectory(prefix='package-smoke-', dir=root / 'artifacts') as work:
    work=Path(work)
    env['NUGET_PACKAGES'] = str(work / '.packages')
    (work/'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="local" value="'+html.escape(str(out), quote=True)+'"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
    run('dotnet','new','console','--framework','net10.0',cwd=work)
    run('dotnet','add','package','Minicon.FileCleanUp','--version',version,cwd=work)
    (work/'Program.cs').write_text('''using Minicon.FileCleanUp;
using System.IO.Abstractions;
foreach (var name in new[] { "CleanupOptions", "CleanupRuleOptions", "CleanupStatistics", "CleanupResult", "AuditRecord", "IFileCleanUpService", "ICleanupAuditJournal", "ICleanupAuditRecoveryService", "LocalAuditJournal", "CleanupAuditRecoveryService", "CleanupConfigurationValidation", "IRetryJitter", "RandomRetryJitter" })
    if (Type.GetType($"Minicon.FileCleanUp.{name}, Minicon.FileCleanUp", throwOnError: true) is null)
        throw new Exception("Type forwarding failed: " + name);
var root = Path.Combine(Directory.GetCurrentDirectory(), "minicon-package-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    var data = Path.Combine(root, "data"); Directory.CreateDirectory(data);
    var audit = Path.Combine(root, "audit"); Directory.CreateDirectory(audit);
    var file = Path.Combine(data, "old.txt"); File.WriteAllText(file, "old");
    File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    var options = new CleanupOptions { Audit = new() { Mode = AuditMode.Required, JournalDirectory = audit },
        Rules = [new() { Name = "Smoke", RetentionDays = 14, Directories = [new() { Root = data }] }] };
    IFileCleanUpService service = new FileCleanUpService(options, new FileSystem(), TimeProvider.System);
    var result = await service.RunAsync();
    if (result.Status != CleanupStatus.Succeeded || result.Statistics.CandidateFiles != 1 || !File.Exists(file))
        throw new Exception("Packaged dry run failed: " + System.Text.Json.JsonSerializer.Serialize(result));
    ICleanupAuditRecoveryService recovery = new CleanupAuditRecoveryService(audit, new FileSystem(), TimeProvider.System);
    if (recovery.Inspect().Count != 0) throw new Exception("Unexpected pending audit");
}
finally { Directory.Delete(root, true); }
''')
    run('dotnet','build',cwd=work)
    run('dotnet','run','--no-build',cwd=work)
    assert (work/'bin/Debug/net10.0/docs/Minicon.FileCleanUp/index.html').is_file()
    run('dotnet','publish','-o',str(work/'publish'),cwd=work)
    assert (work/'publish/docs/Minicon.FileCleanUp/index.html').is_file()
    run('dotnet','publish','-p:MiniconFileCleanUpCopyDocumentation=false','-o',str(work/'without-docs'),cwd=work)
    assert not (work/'without-docs/docs/Minicon.FileCleanUp/index.html').exists()
print('Package installation, build, publish and documentation opt-out passed.')
