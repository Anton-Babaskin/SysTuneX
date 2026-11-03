using System.Diagnostics;
using SysTuneX.Core.Native;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The cleaner runs as administrator over folders any user can write to - %SystemRoot%\Temp is
/// the obvious one. These pin down that a junction planted in such a folder cannot turn a delete
/// there into a delete somewhere else.
/// </summary>
public sealed class ContainedDeleteTests : IDisposable
{
    private readonly string _directory;
    private readonly string _root;
    private readonly string _outside;

    public ContainedDeleteTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "systunex-tests", Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_directory, "root");
        _outside = Path.Combine(_directory, "outside");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        try
        {
            // Links first, one at a time. A recursive delete would not walk into them, but it
            // unmounts a junction as if it were a volume, which a standard user may not do.
            foreach (string link in (string[])[Path.Combine(_root, "x"), Path.Combine(_directory, "linked-root")])
            {
                if (Directory.Exists(link))
                {
                    Directory.Delete(link);
                }
            }

            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    [Fact]
    public void A_file_inside_the_root_is_deleted()
    {
        string file = Touch(Path.Combine(_root, "setup.tmp"));

        Assert.Equal(ContainedDelete.Outcome.Deleted, ContainedDelete.Delete(file, ResolvedRoot(), directory: false));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void A_read_only_file_is_deleted_too()
    {
        string file = Touch(Path.Combine(_root, "shader.bin"));
        File.SetAttributes(file, FileAttributes.ReadOnly);

        Assert.Equal(ContainedDelete.Outcome.Deleted, ContainedDelete.Delete(file, ResolvedRoot(), directory: false));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void A_file_reached_through_a_junction_is_left_alone()
    {
        string victim = Touch(Path.Combine(_outside, "victim.dll"));
        string root = ResolvedRoot();

        // The swap the old cleaner could not see: a folder inside the root that now leads out of it.
        string link = Path.Combine(_root, "x");
        CreateJunction(link, _outside);

        Assert.Equal(
            ContainedDelete.Outcome.OutsideRoot,
            ContainedDelete.Delete(Path.Combine(link, "victim.dll"), root, directory: false));
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public void A_root_that_is_itself_a_junction_is_refused()
    {
        string link = Path.Combine(_directory, "linked-root");
        CreateJunction(link, _outside);

        Assert.Null(ContainedDelete.ResolveRoot(link));
    }

    [Fact]
    public void A_folder_with_something_in_it_is_kept()
    {
        string folder = Path.Combine(_root, "sub");
        Touch(Path.Combine(folder, "keep.txt"));

        Assert.Equal(ContainedDelete.Outcome.Failed, ContainedDelete.Delete(folder, ResolvedRoot(), directory: true));
        Assert.True(Directory.Exists(folder));
    }

    private string ResolvedRoot()
    {
        string? root = ContainedDelete.ResolveRoot(_root);
        Assert.NotNull(root);
        return root;
    }

    private static string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    /// <summary>mklink /J needs no privilege at all, which is the whole problem.</summary>
    private static void CreateJunction(string link, string target)
    {
        var startInfo = new ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process process = Process.Start(startInfo)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
