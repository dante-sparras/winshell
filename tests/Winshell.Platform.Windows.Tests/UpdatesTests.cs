using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Winshell.Platform.Windows.Services.Updates;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// The portable build's updates. GitHub is a fake that serves a release and its zip; every folder is a temp folder of
/// the test's own. Nothing is downloaded from the network and no installed Winshell is touched.
/// </summary>
public sealed class UpdatesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-updates-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _warnings = [];
    private readonly Log _log;

    public UpdatesTests()
    {
        Directory.CreateDirectory(InstallDir);
        _log = new Log { Sink = (level, category, message) => { if (level >= LogLevel.Warn) _warnings.Add($"{category}: {message}"); } };
    }

    private string InstallDir => Path.Combine(_root, "install");

    private string UpdatesDir => Path.Combine(_root, "updates");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task A_newer_release_downloads_with_its_hash_checked_and_waits_to_apply()
    {
        var zip = Zip(("winshell.exe", "new"), ("sub/winshell.dll", "new dll"));
        var github = GitHub("v0.6.0", ("winshell-0.6.0-win-x64.zip", zip, Sha(zip)), ("winshell-0.6.0-win-arm64.zip", [1], Sha([1])));
        using var updater = Create(github);

        await updater.CheckAsync();

        Assert.Equal(new UpdateState(UpdateStatus.Ready, "0.6.0", ""), updater.State);
        Assert.Equal("new dll", File.ReadAllText(Path.Combine(UpdatesDir, "0.6.0", "sub", "winshell.dll")));
        // Only this architecture's zip, and nothing half-done left behind.
        Assert.Equal(["/repos/dante-sparras/winshell/releases/latest", "/download/winshell-0.6.0-win-x64.zip"], github.Requests);
        Assert.Equal(["0.6.0"], Directory.EnumerateFileSystemEntries(UpdatesDir).Select(Path.GetFileName));
        Assert.Empty(_warnings);

        // A later updater, as after a restart, finds it ready without asking GitHub.
        using var next = Create(Offline());
        Assert.Equal(UpdateStatus.Ready, next.State.Status);
        Assert.Equal("0.6.0", next.State.Version);
    }

    [Fact]
    public async Task A_download_whose_hash_differs_is_deleted_and_never_installed()
    {
        var zip = Zip(("winshell.exe", "tampered"));
        using var updater = Create(GitHub("v0.6.0", ("winshell-0.6.0-win-x64.zip", zip, Sha(Zip(("winshell.exe", "new"))))));

        await updater.CheckAsync();

        Assert.Equal(UpdateStatus.Failed, updater.State.Status);
        Assert.Contains("SHA-256", updater.State.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(UpdatesDir));
        Assert.Null(UpdateInstall.Staged(UpdatesDir, new Version(0, 5, 0)));
    }

    [Fact]
    public async Task A_failed_download_leaves_another_shells_download_alone()
    {
        // Another shell, downloading the same release at the same time, has unzipped part of it. These are the names
        // every download of 0.6.0 used before downloads had names of their own.
        var theirs = Path.Combine(UpdatesDir, "0.6.0.partial");
        Directory.CreateDirectory(theirs);
        File.WriteAllText(Path.Combine(theirs, "winshell.exe"), "new");
        File.WriteAllText(Path.Combine(UpdatesDir, "0.6.0.zip.partial"), "");
        var zip = Zip(("winshell.exe", "tampered"));
        using var updater = Create(GitHub("v0.6.0", ("winshell-0.6.0-win-x64.zip", zip, Sha(Zip(("winshell.exe", "new"))))));

        await updater.CheckAsync();

        Assert.Equal(UpdateStatus.Failed, updater.State.Status);
        Assert.True(File.Exists(Path.Combine(theirs, "winshell.exe")));
        Assert.True(File.Exists(Path.Combine(UpdatesDir, "0.6.0.zip.partial")));
        // Neither is an update to install.
        Assert.Null(UpdateInstall.Staged(UpdatesDir, new Version(0, 5, 0)));
    }

    [Theory]
    [InlineData("v0.5.0")]
    [InlineData("v0.4.9")]
    public async Task The_same_or_an_older_release_is_up_to_date_and_downloads_nothing(string tag)
    {
        var github = GitHub(tag, ("winshell-x-win-x64.zip", [1], Sha([1])));
        using var updater = Create(github);

        await updater.CheckAsync();

        Assert.Equal(UpdateStatus.UpToDate, updater.State.Status);
        Assert.Equal(tag[1..], updater.State.Version);
        Assert.Single(github.Requests);
    }

    [Fact]
    public async Task A_release_without_a_digest_or_a_zip_for_this_architecture_is_not_installed()
    {
        using var noDigest = Create(GitHub("v0.6.0", ("winshell-0.6.0-win-x64.zip", [1], null)));
        await noDigest.CheckAsync();
        Assert.Equal(UpdateStatus.Failed, noDigest.State.Status);
        Assert.Contains("no SHA-256 digest", noDigest.State.Error, StringComparison.Ordinal);

        using var noZip = Create(GitHub("v0.6.0", ("winshell-0.6.0-win-arm64.zip", [1], Sha([1]))));
        await noZip.CheckAsync();
        Assert.Equal(UpdateStatus.Failed, noZip.State.Status);
        Assert.Contains("no zip for win-x64", noZip.State.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(UpdatesDir));
    }

    [Fact]
    public async Task A_failed_check_reports_why_but_keeps_a_ready_update_ready()
    {
        using var offline = Create(Offline());
        await offline.CheckAsync();
        Assert.Equal(UpdateStatus.Failed, offline.State.Status);
        Assert.StartsWith("Winshell could not check for updates", offline.State.Error, StringComparison.Ordinal);

        Stage("0.6.0", ("winshell.exe", "new"));
        using var ready = Create(Offline());
        await ready.CheckAsync();
        Assert.Equal(new UpdateState(UpdateStatus.Ready, "0.6.0", ""), ready.State);
    }

    [Fact]
    public async Task An_update_already_downloaded_is_not_downloaded_again()
    {
        Stage("0.6.0", ("winshell.exe", "new"));
        var github = GitHub("v0.6.0", ("winshell-0.6.0-win-x64.zip", [1], Sha([1])));
        using var updater = Create(github);

        await updater.CheckAsync();

        Assert.Equal(new UpdateState(UpdateStatus.Ready, "0.6.0", ""), updater.State);
        Assert.Single(github.Requests);
    }

    [Fact]
    public void Install_replaces_the_files_the_update_has_and_keeps_the_old_ones_until_the_next_start()
    {
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "old");
        File.WriteAllText(Path.Combine(InstallDir, "notes.txt"), "the user's");
        var staged = Stage("0.6.0", ("winshell.exe", "new"), ("sub/winshell.dll", "new dll"));
        // As a running winshell.exe is: open, but movable.
        using (new FileStream(Path.Combine(InstallDir, "winshell.exe"), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            Assert.Null(UpdateInstall.Apply(staged, InstallDir));

        Assert.Equal("new", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
        Assert.Equal("new dll", File.ReadAllText(Path.Combine(InstallDir, "sub", "winshell.dll")));
        Assert.Equal("the user's", File.ReadAllText(Path.Combine(InstallDir, "notes.txt")));
        Assert.False(Directory.Exists(staged));
        var old = Assert.Single(Directory.GetFiles(Path.Combine(InstallDir, UpdateInstall.OldFolder), "winshell.exe", SearchOption.AllDirectories));
        Assert.Equal("old", File.ReadAllText(old));

        UpdateInstall.CleanUp(InstallDir, UpdatesDir, new Version(0, 6, 0));
        Assert.False(Directory.Exists(Path.Combine(InstallDir, UpdateInstall.OldFolder)));
    }

    [Fact]
    public void A_failed_install_puts_every_file_back()
    {
        foreach (var name in new[] { "a.dll", "b.dll", "c.dll" })
            File.WriteAllText(Path.Combine(InstallDir, name), "old " + name);
        var staged = Stage("0.6.0", ("a.dll", "new"), ("b.dll", "new"), ("c.dll", "new"), ("d.dll", "new"));

        string? error;
        // Opened without FILE_SHARE_DELETE, so it cannot be moved aside.
        using (new FileStream(Path.Combine(InstallDir, "b.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            error = UpdateInstall.Apply(staged, InstallDir);

        Assert.NotNull(error);
        Assert.Equal(["a.dll", "b.dll", "c.dll"], Directory.GetFiles(InstallDir).Select(Path.GetFileName).Order());
        foreach (var name in new[] { "a.dll", "b.dll", "c.dll" })
            Assert.Equal("old " + name, File.ReadAllText(Path.Combine(InstallDir, name)));
        // Still there to try again at the next start.
        Assert.True(File.Exists(Path.Combine(staged, "d.dll")));
    }

    [Fact]
    public void The_newest_complete_update_newer_than_this_one_is_the_one_to_install()
    {
        Stage("0.5.0", ("winshell.exe", "same"));
        Stage("0.6.0", ("winshell.exe", "new"));
        Stage("0.7.0", ("winshell.exe", "newer"));
        Stage("0.8.0", ("readme.txt", "no exe: never finished"));
        Directory.CreateDirectory(Path.Combine(UpdatesDir, "0.9.0.partial"));
        File.WriteAllText(Path.Combine(UpdatesDir, "0.9.0.zip.partial"), "");

        Assert.Equal(new Version(0, 7, 0), UpdateInstall.Staged(UpdatesDir, new Version(0, 5, 0))?.Version);

        UpdateInstall.CleanUp(InstallDir, UpdatesDir, new Version(0, 6, 0));
        Assert.Equal(["0.7.0", "0.8.0"], Directory.EnumerateFileSystemEntries(UpdatesDir).Select(Path.GetFileName).Order());
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("0.5.0+fd93d38", "0.5.0")]
    [InlineData("2.0.0-beta.1", "2.0.0")]
    [InlineData("1.2", null)]
    [InlineData("latest", null)]
    public void Versions_read_as_major_minor_patch(string text, string? expected) =>
        Assert.Equal(expected, Updater.ParseVersion(text)?.ToString(3));

    [Fact]
    public void A_winget_packages_or_links_folder_is_not_a_copy_Winshell_updates()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var packages = Path.Combine(local, "Microsoft", "WinGet", "Packages", "DanteSparras.Winshell");
        var links = Path.Combine(local, "Microsoft", "WinGet", "Links");
        var portable = Path.Combine(local, "Programs", "winshell", "bin");
        var lookalike = Path.Combine(local, "Microsoft", "WinGet2", "Packages", "DanteSparras.Winshell");
        Assert.True(Updater.IsWingetManaged(packages));
        Assert.True(Updater.IsWingetManaged(links));
        Assert.False(Updater.IsWingetManaged(portable));
        Assert.False(Updater.IsWingetManaged(lookalike));
    }

    private const string Qml = """
        import QtQuick
        import Winshell
        Item {
          property bool available: Updates.available
          property bool packaged: Updates.packaged
          property bool automatic: Updates.automatic
          property int status: Updates.status
          property bool ready: Updates.status === UpdateStatus.Ready
          property string version: Updates.version
          property bool want: false
          property int restarts: 0
          onWantChanged: Updates.automatic = want
          onRestartsChanged: Updates.restart()
        }
        """;

    [Fact]
    public void QML_sees_a_ready_update_and_restarts_through_the_host()
    {
        Stage("0.6.0", ("winshell.exe", "new"));
        using var updater = Create(Offline());
        var restarts = 0;
        updater.Restart = () =>
        {
            restarts++;
            return null;
        };
        var root = Load(updater);

        Assert.True((bool)root.ReadProperty("available")!);
        Assert.False((bool)root.ReadProperty("packaged")!);
        Assert.True((bool)root.ReadProperty("ready")!);
        Assert.Equal("0.6.0", root.ReadProperty("version"));

        Assert.False((bool)root.ReadProperty("automatic")!);
        root.WriteProperty("want", true);
        Assert.True(updater.Automatic);
        Assert.True((bool)root.ReadProperty("automatic")!);
        root.WriteProperty("want", false);
        Assert.False(updater.Automatic);

        root.WriteProperty("restarts", 1);
        Assert.Equal(1, restarts);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Without_an_updater_QML_sees_a_Winshell_that_does_not_update_itself()
    {
        var root = Load(null);

        Assert.False((bool)root.ReadProperty("available")!);
        Assert.Equal((int)UpdateStatus.Idle, root.ReadProperty("status"));
        root.WriteProperty("restarts", 1);
        Assert.Contains(_warnings, w => w.Contains("no update is ready", StringComparison.Ordinal));
    }

    [Fact]
    public void Restart_does_nothing_until_an_update_is_ready()
    {
        using var updater = Create(Offline());
        var restarts = 0;
        updater.Restart = () =>
        {
            restarts++;
            return null;
        };

        Assert.False(updater.RestartToUpdate());
        Assert.Equal(0, restarts);
    }

    [Fact]
    public void A_refused_restart_keeps_the_update_ready_and_says_why()
    {
        Stage("0.6.0", ("winshell.exe", "new"));
        using var updater = Create(Offline());
        updater.Restart = () => "Another Winshell is running. Close it, then restart this one.";

        Assert.True(updater.RestartToUpdate());
        Assert.Equal(new UpdateState(UpdateStatus.Ready, "0.6.0", "Another Winshell is running. Close it, then restart this one."), updater.State);
        Assert.Contains(_warnings, w => w.Contains("Another Winshell is running", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_checked_download_keeps_its_zip_beside_the_updates_folder()
    {
        var zip = Zip(("winshell.exe", "new"));
        using var updater = Create(GitHub("v0.6.0", ("winshell-0.6.0-win-x64.zip", zip, Sha(zip))));

        await updater.CheckAsync();

        Assert.Equal(["0.6.0"], Directory.EnumerateFileSystemEntries(UpdatesDir).Select(Path.GetFileName));
        var saved = UpdateTrust.ZipPath(UpdatesDir, "0.6.0");
        Assert.Equal(Sha(File.ReadAllBytes(saved)), Sha(zip));
        Assert.Equal(Sha(zip)["sha256:".Length..], File.ReadAllText(UpdateTrust.DigestPath(UpdatesDir, "0.6.0")).Trim());
    }

    [Fact]
    public async Task A_swapped_staged_file_is_not_what_gets_installed()
    {
        var zip = Zip(("winshell.exe", "from-zip"));
        using var updater = Create(GitHub("v0.6.0", ("winshell-0.6.0-win-x64.zip", zip, Sha(zip))));
        await updater.CheckAsync();
        File.WriteAllText(Path.Combine(UpdatesDir, "0.6.0", "winshell.exe"), "swapped");
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "old");

        Assert.Null(UpdateInstall.ApplyVerified(UpdatesDir, Path.Combine(UpdatesDir, "0.6.0"), InstallDir, privileged: false));

        Assert.Equal("from-zip", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
        Assert.False(Directory.Exists(Path.Combine(UpdatesDir, "0.6.0")));
        Assert.False(File.Exists(UpdateTrust.ZipPath(UpdatesDir, "0.6.0")));
    }

    [Fact]
    public async Task A_changed_zip_is_not_installed_and_the_staged_tree_is_deleted()
    {
        var zip = Zip(("winshell.exe", "from-zip"));
        using var updater = Create(GitHub("v0.6.0", ("winshell-0.6.0-win-x64.zip", zip, Sha(zip))));
        await updater.CheckAsync();
        File.WriteAllBytes(UpdateTrust.ZipPath(UpdatesDir, "0.6.0"), Zip(("winshell.exe", "changed")));
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "old");

        var error = UpdateInstall.ApplyVerified(UpdatesDir, Path.Combine(UpdatesDir, "0.6.0"), InstallDir, privileged: false);

        Assert.Contains("SHA-256", error, StringComparison.Ordinal);
        Assert.Equal("old", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
        Assert.False(Directory.Exists(Path.Combine(UpdatesDir, "0.6.0")));
        Assert.False(File.Exists(UpdateTrust.ZipPath(UpdatesDir, "0.6.0")));
    }

    [Fact]
    public void A_staged_update_without_a_digest_is_not_copied()
    {
        var staged = Stage("0.6.0", ("winshell.exe", "new"));
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "old");

        var error = UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir, privileged: false);

        Assert.Contains("digest", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("old", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
        Assert.False(Directory.Exists(staged));
    }

    [Fact]
    public async Task An_elevated_apply_copies_nothing_and_leaves_the_update_for_a_normal_start()
    {
        var zip = Zip(("winshell.exe", "from-zip"));
        using var updater = Create(GitHub("v0.6.0", ("winshell-0.6.0-win-x64.zip", zip, Sha(zip))));
        await updater.CheckAsync();
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "old");
        var staged = Path.Combine(UpdatesDir, "0.6.0");

        var error = UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir, privileged: true);

        Assert.Equal("An elevated Winshell does not install updates. Start Winshell normally to install it.", error);
        Assert.Equal(["winshell.exe"], Directory.EnumerateFileSystemEntries(InstallDir).Select(Path.GetFileName));
        Assert.Equal("old", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
        Assert.True(Directory.Exists(staged));
        Assert.True(File.Exists(UpdateTrust.ZipPath(UpdatesDir, "0.6.0")));
        Assert.Empty(Directory.EnumerateDirectories(UpdateTrust.Root(UpdatesDir)));
    }

    [Fact]
    public async Task A_zip_another_process_has_open_for_writing_is_not_installed()
    {
        var zip = Zip(("winshell.exe", "from-zip"));
        using var updater = Create(GitHub("v0.6.0", ("winshell-0.6.0-win-x64.zip", zip, Sha(zip))));
        await updater.CheckAsync();
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "old");
        var staged = Path.Combine(UpdatesDir, "0.6.0");

        // A writer that still lets others read. Apply's handle denies writers, so it cannot open the zip while this
        // one is open, and no writer can open the zip while apply holds it between the hash and the extract.
        string? error;
        using (new FileStream(UpdateTrust.ZipPath(UpdatesDir, "0.6.0"), FileMode.Open, FileAccess.Write, FileShare.Read))
            error = UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir, privileged: false);

        Assert.NotNull(error);
        Assert.Equal("old", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
        Assert.True(Directory.Exists(staged));
        Assert.Empty(Directory.EnumerateDirectories(UpdateTrust.Root(UpdatesDir)));
    }

    [Fact]
    public void Clean_up_deletes_the_scratch_a_crash_leaves_beside_the_updates_folder()
    {
        var root = UpdateTrust.Root(UpdatesDir);
        Directory.CreateDirectory(Path.Combine(root, "extract-0123456789abcdef", "sub"));
        File.WriteAllText(Path.Combine(root, "extract-0123456789abcdef", "sub", "winshell.dll"), "half");
        File.WriteAllText(Path.Combine(root, "0.6.0.sha256.partial"), "half");
        File.WriteAllText(Path.Combine(root, "0.5.0.zip"), "installed");
        File.WriteAllText(Path.Combine(root, "0.5.0.sha256"), "installed");
        File.WriteAllText(Path.Combine(root, "0.6.0.zip"), "waiting");
        File.WriteAllText(Path.Combine(root, "0.6.0.sha256"), "waiting");

        UpdateInstall.CleanUp(InstallDir, UpdatesDir, new Version(0, 5, 0));

        Assert.Equal(["0.6.0.sha256", "0.6.0.zip"], Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void A_link_in_the_update_is_not_copied()
    {
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "old");
        var staged = Stage("0.6.0", ("winshell.exe", "new"));
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
        var junction = Path.Combine(staged, "linked");
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        info.ArgumentList.Add("/c");
        info.ArgumentList.Add("mklink");
        info.ArgumentList.Add("/J");
        info.ArgumentList.Add(junction);
        info.ArgumentList.Add(outside);
        using var link = Process.Start(info)!;
        link.WaitForExit();
        Assert.Equal(0, link.ExitCode);
        try
        {
            var error = UpdateInstall.Apply(staged, InstallDir);

            Assert.NotNull(error);
            Assert.False(File.Exists(Path.Combine(InstallDir, "linked", "secret.txt")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public async Task A_signed_install_takes_an_update_its_own_signer_signed()
    {
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "signed by CN=Winshell");
        // A library SignPath does not sign keeps its own publisher's signature, or none, and is not checked.
        var staged = await Download(("winshell.exe", "signed by CN=Winshell"), ("winshell.dll", "signed by CN=Winshell"), ("Jint.dll", "unsigned"));

        Assert.Null(UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir, privileged: false, FakeSigner));

        Assert.Equal("signed by CN=Winshell", File.ReadAllText(Path.Combine(InstallDir, "winshell.dll")));
        Assert.Equal("unsigned", File.ReadAllText(Path.Combine(InstallDir, "Jint.dll")));
        Assert.False(Directory.Exists(staged));
    }

    [Fact]
    public async Task A_signed_install_refuses_an_unsigned_update_and_deletes_it()
    {
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "signed by CN=Winshell");
        var staged = await Download(("winshell.exe", "unsigned"));

        var error = UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir, privileged: false, FakeSigner);

        Assert.Equal("This Winshell is signed by CN=Winshell, but the update's winshell.exe is not validly signed, so Winshell does not install it.", error);
        Assert.Equal("signed by CN=Winshell", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
        Assert.False(Directory.Exists(staged));
        Assert.False(File.Exists(UpdateTrust.ZipPath(UpdatesDir, "0.6.0")));
        Assert.Empty(Directory.EnumerateDirectories(UpdateTrust.Root(UpdatesDir)));
    }

    [Fact]
    public async Task A_signed_install_refuses_an_update_another_signer_signed()
    {
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "signed by CN=Winshell");
        var staged = await Download(("winshell.exe", "signed by CN=Winshell"), ("Winshell.Runtime.dll", "signed by CN=Someone Else"));

        var error = UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir, privileged: false, FakeSigner);

        Assert.Equal("This Winshell is signed by CN=Winshell, but the update's Winshell.Runtime.dll is signed by CN=Someone Else, so Winshell does not install it.", error);
        Assert.Equal(["winshell.exe"], Directory.EnumerateFileSystemEntries(InstallDir).Select(Path.GetFileName));
        Assert.False(Directory.Exists(staged));
    }

    [Fact]
    public async Task An_unsigned_install_takes_an_unsigned_update()
    {
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "unsigned");
        var staged = await Download(("winshell.exe", "new"));

        Assert.Null(UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir, privileged: false, FakeSigner));

        Assert.Equal("new", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
    }

    [Fact]
    public void The_signer_is_read_from_a_valid_embedded_signature_only()
    {
        var unsigned = Path.Combine(_root, "unsigned.dll");
        File.WriteAllText(unsigned, "unsigned");
        var tampered = Path.Combine(_root, "tampered.dll");
        File.Copy(typeof(object).Assembly.Location, tampered);
        // Any change to the signed bytes breaks the signature.
        using (var stream = new FileStream(tampered, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = 0x200;
            var b = stream.ReadByte();
            stream.Position = 0x200;
            stream.WriteByte((byte)(b ^ 0xFF));
        }

        Assert.Null(UpdateSignature.Signer(unsigned));
        Assert.Null(UpdateSignature.Signer(tampered));
        Assert.Null(UpdateSignature.Signer(Path.Combine(_root, "missing.dll")));
        // The .NET runtime's own files carry an embedded Authenticode signature, not a catalog one.
        Assert.Contains("O=Microsoft Corporation", UpdateSignature.Signer(typeof(object).Assembly.Location), StringComparison.Ordinal);
    }

    /// <summary>Downloads 0.6.0 with these files through the fake GitHub, and returns its staged folder.</summary>
    private async Task<string> Download(params (string Name, string Text)[] files)
    {
        var zip = Zip(files);
        using var updater = Create(GitHub("v0.6.0", ("winshell-0.6.0-win-x64.zip", zip, Sha(zip))));
        await updater.CheckAsync();
        Assert.Equal(UpdateStatus.Ready, updater.State.Status);
        return Path.Combine(UpdatesDir, "0.6.0");
    }

    /// <summary>Signs a file by its text: "signed by &lt;subject&gt;". Anything else is unsigned.</summary>
    private static string? FakeSigner(string path) =>
        File.Exists(path) && File.ReadAllText(path) is var text && text.StartsWith("signed by ", StringComparison.Ordinal) ? text["signed by ".Length..] : null;

    private Updater Create(FakeGitHub github) =>
        new("dante-sparras/winshell", InstallDir, UpdatesDir, "0.5.0+test", "win-x64", _log, github);

    private QObject Load(Updater? updater)
    {
        var path = Path.Combine(_root, "shell.qml");
        File.WriteAllText(path, Qml);
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        ServiceModules.Register(registry);
        var loop = new ManualEventLoop();
        var engine = new QmlEngine(registry, loop, _log, _root);
        if (updater is not null)
            WindowsModules.AddUpdates(engine, updater);
        var root = engine.CreateRoot(path);
        loop.Process();
        return root;
    }

    private string Stage(string version, params (string Name, string Text)[] files)
    {
        var dir = Path.Combine(UpdatesDir, version);
        foreach (var (name, text) in files)
        {
            var path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        return dir;
    }

    private static byte[] Zip(params (string Name, string Text)[] files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }
        return stream.ToArray();
    }

    private static string Sha(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>A GitHub whose latest release is <paramref name="tag"/>, with the release JSON's fields the updater reads.</summary>
    private static FakeGitHub GitHub(string tag, params (string Name, byte[] Bytes, string? Digest)[] assets)
    {
        var list = string.Join(",", assets.Select(a =>
            $$"""{"name":"{{a.Name}}","browser_download_url":"https://github.com/download/{{a.Name}}","digest":{{(a.Digest is null ? "null" : $"\"{a.Digest}\"")}}}"""));
        return new FakeGitHub($$"""{"tag_name":"{{tag}}","assets":[{{list}}]}""", assets.ToDictionary(a => "/download/" + a.Name, a => a.Bytes));
    }

    /// <summary>A GitHub that cannot be reached.</summary>
    private static FakeGitHub Offline() => new(null, []);

    /// <summary>Serves the latest release and its files, and records what was asked for.</summary>
    private sealed class FakeGitHub(string? release, Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (release is null)
                throw new HttpRequestException("No such host is known.");
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            if (path.EndsWith("/releases/latest", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(release) });
            return Task.FromResult(files.TryGetValue(path, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
