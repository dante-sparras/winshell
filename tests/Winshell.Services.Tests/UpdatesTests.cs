using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Winshell.Native;
using Winshell.Updates;

namespace Winshell.Services.Tests;

/// <summary>
/// Installing a staged update. Every folder is a temp folder of the test's own. No installed Winshell is touched.
/// </summary>
public sealed class UpdatesTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "winshell-updates-tests", Guid.NewGuid().ToString("N"));

    public UpdatesTests()
    {
        Directory.CreateDirectory(InstallDir);
    }

    private string InstallDir => Path.Combine(_root, "install");

    private string UpdatesDir => Path.Combine(_root, "updates");

    public void Dispose()
    {
        Directory.Delete(_root, true);
    }

    [Fact]
    public void Install_replaces_the_files_the_update_has_and_keeps_the_old_ones_until_the_next_start()
    {
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "old");
        File.WriteAllText(Path.Combine(InstallDir, "notes.txt"), "the user's");
        var staged = Stage("0.6.0", ("winshell.exe", "new"), ("sub/winshell.dll", "new dll"));
        // As a running winshell.exe is: open, but movable.
        using (new FileStream(Path.Combine(InstallDir, "winshell.exe"), FileMode.Open, FileAccess.Read,
                   FileShare.Read | FileShare.Delete))
        {
            Assert.Null(UpdateInstall.Apply(staged, InstallDir));
        }

        Assert.Equal("new", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
        Assert.Equal("new dll", File.ReadAllText(Path.Combine(InstallDir, "sub", "winshell.dll")));
        Assert.Equal("the user's", File.ReadAllText(Path.Combine(InstallDir, "notes.txt")));
        Assert.False(Directory.Exists(staged));
        var old = Assert.Single(Directory.GetFiles(Path.Combine(InstallDir, UpdateInstall.OldFolder), "winshell.exe",
            SearchOption.AllDirectories));
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
        {
            error = UpdateInstall.Apply(staged, InstallDir);
        }

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
        Assert.Equal(["0.7.0", "0.8.0"],
            Directory.EnumerateFileSystemEntries(UpdatesDir).Select(Path.GetFileName).Order());
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("0.5.0+fd93d38", "0.5.0")]
    [InlineData("2.0.0-beta.1", "2.0.0")]
    [InlineData("1.2", null)]
    [InlineData("latest", null)]
    public void Versions_read_as_major_minor_patch(string text, string? expected)
    {
        Assert.Equal(expected, Updater.ParseVersion(text)?.ToString(3));
    }

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

    [Fact]
    public void A_swapped_staged_file_is_not_what_gets_installed()
    {
        var staged = StageVerified(Tag, Zip(("winshell.exe", Exe("from-zip"))));
        File.WriteAllBytes(Path.Combine(staged, "winshell.exe"), Exe("swapped"));
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "old");

        Assert.Null(UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir));

        Assert.Equal("from-zip", Text(Path.Combine(InstallDir, "winshell.exe")));
        Assert.False(Directory.Exists(staged));
        Assert.False(File.Exists(UpdateTrust.ZipPath(UpdatesDir, Tag)));
    }

    [Fact]
    public void A_changed_zip_is_not_installed_and_the_staged_tree_is_deleted()
    {
        var staged = StageVerified(Tag, Zip(("winshell.exe", Exe("from-zip"))));
        File.WriteAllBytes(UpdateTrust.ZipPath(UpdatesDir, Tag), Zip(("winshell.exe", Exe("changed"))));
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "old");

        var error = UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir);

        Assert.Contains("SHA-256", error, StringComparison.Ordinal);
        Assert.Equal("old", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
        Assert.False(Directory.Exists(staged));
        Assert.False(File.Exists(UpdateTrust.ZipPath(UpdatesDir, Tag)));
    }

    [Fact]
    public void A_staged_update_without_a_digest_is_not_copied()
    {
        var staged = Stage("0.6.0", ("winshell.exe", "new"));
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "old");

        var error = UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir);

        Assert.Contains("digest", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("old", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
        Assert.False(Directory.Exists(staged));
    }

    [Fact]
    public void A_zip_another_process_has_open_for_writing_is_not_installed()
    {
        var staged = StageVerified(Tag, Zip(("winshell.exe", Exe("from-zip"))));
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "old");

        // A writer that still lets others read. Apply's handle denies writers, so it cannot open the zip while this
        // one is open, and no writer can open the zip while apply holds it between the hash and the extract.
        string? error;
        using (new FileStream(UpdateTrust.ZipPath(UpdatesDir, Tag), FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            error = UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir);
        }

        Assert.NotNull(error);
        Assert.Equal("old", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
        Assert.True(Directory.Exists(staged));
        Assert.Empty(Directory.EnumerateDirectories(UpdateTrust.Root(UpdatesDir)));
    }

    [Fact]
    public void A_release_whose_binary_is_another_version_than_its_tag_is_not_installed()
    {
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "old");
        var staged = StageVerified("99.0.0", Zip(("winshell.exe", Exe("from-zip"))));

        var error = UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir);

        Assert.Contains("Winshell 99.0.0", error, StringComparison.Ordinal);
        Assert.Equal("old", File.ReadAllText(Path.Combine(InstallDir, "winshell.exe")));
        Assert.False(Directory.Exists(staged));
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

        Assert.Equal(["0.6.0.sha256", "0.6.0.zip"],
            Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).Order());
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
        // mklink prints a line. This process's own output carries xUnit's results, and a line in the middle loses one.
        ChildProcess.Run("cmd.exe", "/c", "mklink", "/J", junction, outside).AssertExitCode(0);
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
    public void A_signed_install_takes_an_update_whose_files_are_all_signed()
    {
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "signed by CN=Winshell");
        var staged = StageVerified(Tag, Zip(SignedRelease(
            ("coreclr.dll", Bytes("signed by CN=Microsoft Corporation, O=Microsoft Corporation")),
            ("readme.txt", Bytes("unsigned")))));

        Assert.Null(UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir, FakeSigner));

        Assert.Equal("signed by CN=Microsoft Corporation, O=Microsoft Corporation",
            File.ReadAllText(Path.Combine(InstallDir, "coreclr.dll")));
        Assert.Equal("unsigned", File.ReadAllText(Path.Combine(InstallDir, "readme.txt")));
        Assert.False(Directory.Exists(staged));
    }

    [Theory]
    [InlineData("hostfxr.dll", "unsigned", "is not validly signed")]
    [InlineData("version.dll", "unsigned", "is not validly signed")]
    [InlineData("Winshell.Shell.dll", "signed by CN=Someone Else", "is signed by CN=Someone Else")]
    [InlineData("sub/helper.exe", "signed by CN=Someone Else", "is signed by CN=Someone Else")]
    [InlineData("hostfxr.dll", "signed by CN=\"Evil, O=Microsoft Corporation\", O=Evil", "is signed by")]
    public void A_signed_install_refuses_an_update_with_any_binary_signed_by_another_or_nobody(string name,
        string text, string why)
    {
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "signed by CN=Winshell");
        var staged = StageVerified(Tag, Zip(SignedRelease((name, Bytes(text)))));

        var error = UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir, FakeSigner);

        Assert.Contains($"the update's {name.Replace('/', Path.DirectorySeparatorChar)} {why}", error,
            StringComparison.Ordinal);
        Assert.Equal(["winshell.exe"], Directory.EnumerateFileSystemEntries(InstallDir).Select(Path.GetFileName));
        Assert.False(Directory.Exists(staged));
        Assert.False(File.Exists(UpdateTrust.ZipPath(UpdatesDir, Tag)));
        Assert.Empty(Directory.EnumerateDirectories(UpdateTrust.Root(UpdatesDir)));
    }

    [Theory]
    [InlineData("winshellw.exe")]
    [InlineData("Winshell.Native.dll")]
    public void A_signed_install_refuses_an_update_that_leaves_out_a_signed_binary(string name)
    {
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "signed by CN=Winshell");
        var staged = StageVerified(Tag, Zip(SignedRelease().Where(file => file.Name != name).ToArray()));

        var error = UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir, FakeSigner);

        Assert.Equal($"This Winshell is signed, but the update has no {name}, so Winshell does not install it.", error);
        Assert.Equal(["winshell.exe"], Directory.EnumerateFileSystemEntries(InstallDir).Select(Path.GetFileName));
        Assert.False(Directory.Exists(staged));
    }

    [Fact]
    public void An_unsigned_install_takes_an_unsigned_update()
    {
        File.WriteAllText(Path.Combine(InstallDir, "winshell.exe"), "unsigned");
        var staged = StageVerified(Tag, Zip(("winshell.exe", Exe("new")), ("coreclr.dll", Bytes("unsigned"))));

        Assert.Null(UpdateInstall.ApplyVerified(UpdatesDir, staged, InstallDir, FakeSigner));

        Assert.Equal("new", Text(Path.Combine(InstallDir, "winshell.exe")));
        Assert.Equal("unsigned", File.ReadAllText(Path.Combine(InstallDir, "coreclr.dll")));
    }

    [Fact]
    public void Microsoft_is_the_organization_of_a_signing_subject_not_text_inside_another_name()
    {
        Assert.True(
            UpdateSignature.IsMicrosoft(
                "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"));
        Assert.False(UpdateSignature.IsMicrosoft("CN=\"Evil, O=Microsoft Corporation\", O=Evil"));
        Assert.False(UpdateSignature.IsMicrosoft("CN=Winshell"));
        Assert.False(UpdateSignature.IsMicrosoft(null));
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

        Assert.Null(Authenticode.Signer(unsigned));
        Assert.Null(Authenticode.Signer(tampered));
        Assert.Null(Authenticode.Signer(Path.Combine(_root, "missing.dll")));
        // The .NET runtime's own files carry an embedded Authenticode signature, not a catalog one.
        Assert.Contains("O=Microsoft Corporation", Authenticode.Signer(typeof(object).Assembly.Location),
            StringComparison.Ordinal);
    }

    /// <summary>The release version the test assembly carries, which stands in for a release's <c>winshell.exe</c>.</summary>
    private static readonly string Tag =
        Updater.ParseVersion(FileVersionInfo.GetVersionInfo(typeof(UpdatesTests).Assembly.Location).ProductVersion!)!
            .ToString(3);

    /// <summary>A real, versioned executable with <paramref name="text"/> after its end, which <see cref="Text"/> reads back.</summary>
    private static byte[] Exe(string text)
    {
        return [.. File.ReadAllBytes(typeof(UpdatesTests).Assembly.Location), 1, .. Encoding.Latin1.GetBytes(text)];
    }

    private static byte[] Bytes(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>Signs a file by its text: "signed by &lt;subject&gt;". Anything else is unsigned.</summary>
    private static string? FakeSigner(string path)
    {
        return File.Exists(path) && Text(path) is var text && text.StartsWith("signed by ", StringComparison.Ordinal)
            ? text["signed by ".Length..]
            : null;
    }

    /// <summary>The text of a file made with <see cref="Exe"/>, or all of a plain text file.</summary>
    private static string Text(string path)
    {
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        return text[(text.LastIndexOf('\u0001') + 1)..];
    }

    /// <summary>A signed release's files: every binary SignPath signs, signed by CN=Winshell, and <paramref name="more"/>.</summary>
    private static (string Name, byte[] Bytes)[] SignedRelease(params (string Name, byte[] Bytes)[] more)
    {
        return
        [
            .. UpdateSignature.Signed.Where(name => more.All(m => m.Name != name))
                .Select(name => (name, Exe("signed by CN=Winshell"))),
            .. more
        ];
    }

    /// <summary>
    /// Leaves the zip and its digest where apply reads them, and unzips a copy into the version folder. Apply hashes
    /// the saved zip again, so a later change to the folder is not what gets copied.
    /// </summary>
    private string StageVerified(string version, byte[] zip)
    {
        var held = Path.Combine(_root, version + ".zip");
        File.WriteAllBytes(held, zip);
        var staged = Path.Combine(UpdatesDir, version);
        ZipFile.ExtractToDirectory(held, staged);
        Directory.CreateDirectory(UpdateTrust.Root(UpdatesDir));
        File.WriteAllBytes(UpdateTrust.ZipPath(UpdatesDir, version), zip);
        File.WriteAllText(UpdateTrust.DigestPath(UpdatesDir, version), Convert.ToHexStringLower(SHA256.HashData(zip)));
        return staged;
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
        return Zip([.. files.Select(f => (f.Name, Encoding.UTF8.GetBytes(f.Text)))]);
    }

    private static byte[] Zip(params (string Name, byte[] Bytes)[] files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (var (name, bytes) in files)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(bytes);
            }
        }

        return stream.ToArray();
    }
}
