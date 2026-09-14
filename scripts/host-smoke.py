"""Disposable real-process acceptance: host protection, dry run, deletion, durable intent after kill."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import threading

repo = Path(__file__).resolve().parents[1]
def run(*args, **kwargs):
    result = subprocess.run(args, text=True, **kwargs)
    if result.returncode:
        raise RuntimeError(f"{args} failed ({result.returncode})\n{result.stdout}\n{result.stderr}")
    return result

(repo / "artifacts").mkdir(exist_ok=True)
with tempfile.TemporaryDirectory(prefix="minicon-host-", dir=repo / "artifacts") as temp:
    work = Path(temp)
    published = work / "app"
    run("dotnet", "publish", str(repo / "samples/Minicon.FileCleanUp.Console"), "-c", "Release", "-o", str(published))
    data, state, journal = [work / name for name in ("data", "state", "journal")]
    data.mkdir(); journal.mkdir()
    old = data / "old.csv"
    old.write_text("old")
    os.utime(old, (1577836800, 1577836800))
    settings = {"CleanupHost": {"StateDirectory": str(state)}, "FileCleanUp": {
        "DryRun": True, "Audit": {"Mode": "Required", "JournalDirectory": str(journal)},
        "Rules": [{"Name": "Acceptance", "RetentionDays": 90, "Directories": [{"Root": str(data)}]}]}}
    config = published / "appsettings.json"
    def configure():
        config.write_text(json.dumps(settings))
    def host(*args):
        return run("dotnet", str(published / "Minicon.FileCleanUp.Console.dll"), *args, capture_output=True)
    configure(); host(); assert old.exists()
    settings["FileCleanUp"]["DryRun"] = False
    settings["CleanupHost"]["StateDirectory"] = str(data / "unsafe-state")
    configure()
    rejected = subprocess.run(["dotnet", str(published / "Minicon.FileCleanUp.Console.dll")], capture_output=True)
    assert rejected.returncode == 2 and old.exists() and not (data / "unsafe-state").exists()
    settings["CleanupHost"]["StateDirectory"] = str(state)
    configure(); host(); assert not old.exists()
    reports = [json.loads(p.read_text()) for p in state.glob("*.json")]
    assert sorted(r["Statistics"]["FilesDeleted"] for r in reports) == [0, 1]

    # The probe uses the public package boundary and is killed without Dispose/finally.
    probe = work / "probe"
    run("dotnet", "new", "console", "-o", str(probe), "--framework", "net10.0")
    for name in ["Minicon.FileCleanUp", "Minicon.FileCleanUp.Domain", "Minicon.FileCleanUp.Core", "Minicon.FileCleanUp.Infrastructure"]:
        run("dotnet", "add", str(probe / "probe.csproj"), "reference", str(repo / "src" / name / f"{name}.csproj"))
    (probe / "Program.cs").write_text('''using Minicon.FileCleanUp;
using System.IO.Abstractions;
using var journal = new LocalAuditJournal(args[0], new FileSystem(), maxSegmentBytes: 1024);
for (var i = 0; i < 20; i++) journal.Append(new AuditRecord { Type = "RunStarted", Reason = new string('x', 300) });
journal.Append(new AuditRecord { Type = "DeleteIntent", OperationId = Guid.NewGuid(), Path = args[1] });
Console.WriteLine("INTENT_DURABLE"); Console.Out.Flush();
Thread.Sleep(Timeout.Infinite);
''')
    run("dotnet", "build", str(probe), "-c", "Release")
    process = subprocess.Popen(["dotnet", str(probe / "bin/Release/net10.0/probe.dll"), str(journal), str(old)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    watchdog = threading.Timer(30, process.kill); watchdog.start()
    try:
        assert process.stdout.readline().strip() == "INTENT_DURABLE"
    finally:
        process.kill(); process.wait(timeout=10); watchdog.cancel()
    pending = json.loads(host("--audit-list").stdout)
    assert len(pending) == 1 and pending[0]["Path"] == str(old)
    old.write_text("replacement after interruption")
    os.utime(old, (1577836800, 1577836800))
    blocked = subprocess.run(["dotnet", str(published / "Minicon.FileCleanUp.Console.dll")], capture_output=True)
    assert blocked.returncode == 1 and old.exists()
    host("--audit-ack", pending[0]["OperationId"], "--operator", "acceptance-test", "--reason", "test-owned replacement reviewed")
    host(); assert not old.exists()
print("PASS: host isolation, dry run, real deletion, killed-process recovery and pending-path protection")
