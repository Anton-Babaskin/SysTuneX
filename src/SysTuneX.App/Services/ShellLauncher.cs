using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Models;

namespace SysTuneX.App.Services;

/// <summary>
/// Hands something to Windows to open: a folder, a file, a web page.
///
/// A seam rather than a convenience. Four view models called <c>Process.Start</c> directly, which
/// puts "ask Windows to run something" in the layer whose job is to describe a screen - and made
/// those commands unreachable from any test, because running one opens a window on the machine
/// doing the testing.
/// </summary>
public interface IShellLauncher
{
    /// <summary>Opens a folder, creating it first if it is not there yet.</summary>
    OperationResult OpenFolder(string path);

    /// <summary>Opens Explorer with <paramref name="filePath"/> already selected.</summary>
    OperationResult RevealFile(string filePath);

    /// <summary>Opens a web address in the default browser. Refuses anything that is not http(s).</summary>
    OperationResult OpenUrl(string url);
}

/// <inheritdoc cref="IShellLauncher"/>
public sealed class ShellLauncher : IShellLauncher
{
    private readonly ILogger<ShellLauncher> _logger;

    public ShellLauncher(ILogger<ShellLauncher> logger) => _logger = logger;

    public OperationResult OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            return Failed(ex, "open the folder {Path}", path);
        }
    }

    public OperationResult RevealFile(string filePath)
    {
        try
        {
            // The path is quoted because it comes from a directory the user can rename, and
            // explorer takes everything after the comma as one argument only if it is quoted.
            return Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            return Failed(ex, "reveal the file {Path}", filePath);
        }
    }

    public OperationResult OpenUrl(string url)
    {
        // UseShellExecute hands the string to Windows to interpret, so anything that is not plainly
        // a web address would be something else being run. Every URL here is a constant in our own
        // source, and that is exactly the kind of thing that stops being true one edit later.
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogWarning("Refused to open {Url}: only http and https are allowed", url);
            return OperationResult.Fail(CoreMessages.ShellRefusedAddress, url);
        }

        try
        {
            return Start(new ProcessStartInfo { FileName = parsed.AbsoluteUri, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            return Failed(ex, "open the address {Url}", url);
        }
    }

    private static OperationResult Start(ProcessStartInfo startInfo)
    {
        using Process? process = Process.Start(startInfo);
        return OperationResult.Ok();
    }

    private OperationResult Failed(Exception exception, string what, string argument)
    {
        _logger.LogWarning(exception, "Could not " + what, argument);
        return OperationResult.Fail(CoreMessages.ShellOpenFailed, argument, exception.Message);
    }
}
