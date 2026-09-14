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
    assert 'docs/index.html' in z.namelist(), 'Offline HTML missing from package'
    assert 'build/Minicon.FileCleanUp.targets' in z.namelist(), 'Copy target missing'
    import xml.etree.ElementTree as ET
    spec=ET.fromstring(z.read(next(n for n in z.namelist() if n.endswith('.nuspec'))))
    version=next(e.text for e in spec.iter() if e.tag.endswith('}version') or e.tag=='version')
with tempfile.TemporaryDirectory(prefix='package-smoke-', dir=root / 'artifacts') as work:
    work=Path(work)
    env['NUGET_PACKAGES'] = str(work / '.packages')
    (work/'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="local" value="'+html.escape(str(out), quote=True)+'"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
    run('dotnet','new','console','--framework','net10.0',cwd=work)
    run('dotnet','add','package','Minicon.FileCleanUp','--version',version,cwd=work)
    (work/'Program.cs').write_text('using Minicon.FileCleanUp;\nConsole.WriteLine(new CleanupOptions().DryRun);\n')
    run('dotnet','build',cwd=work)
    assert (work/'bin/Debug/net10.0/docs/Minicon.FileCleanUp/index.html').is_file()
    run('dotnet','publish','-o',str(work/'publish'),cwd=work)
    assert (work/'publish/docs/Minicon.FileCleanUp/index.html').is_file()
    run('dotnet','publish','-p:MiniconFileCleanUpCopyDocumentation=false','-o',str(work/'without-docs'),cwd=work)
    assert not (work/'without-docs/docs/Minicon.FileCleanUp/index.html').exists()
print('Package installation, build, publish and documentation opt-out passed.')
