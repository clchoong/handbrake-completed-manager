using System.Diagnostics;
using HandBrakeCompletedManager.Core;
using Microsoft.Win32;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class HandBrakeDetector
{
    private const string ExecutableName = "HandBrake.exe";

    public Task<IReadOnlyList<HandBrakeInstallation>> DetectAsync(
        IEnumerable<string>? userSearchLocations = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Detect(userSearchLocations ?? [], cancellationToken),
            cancellationToken);

    private static IReadOnlyList<HandBrakeInstallation> Detect(
        IEnumerable<string> userSearchLocations,
        CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);

        AddRunningProcesses(candidates, cancellationToken);
        AddStandardLocations(candidates);
        AddRegistryLocations(candidates, cancellationToken);
        AddUserLocations(candidates, userSearchLocations, cancellationToken);

        return candidates.Values
            .Where(candidate => File.Exists(candidate.Path))
            .Select(CreateInstallation)
            .OrderByDescending(installation => installation.IsRunning)
            .ThenBy(installation => installation.Type)
            .ThenBy(installation => installation.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddRunningProcesses(
        IDictionary<string, Candidate> candidates,
        CancellationToken cancellationToken)
    {
        foreach (var process in Process.GetProcessesByName("HandBrake"))
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var path = process.MainModule?.FileName;
                    AddCandidate(candidates, path, isRunning: true, isInstalled: null, "Running process");
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
                {
                    // Some processes do not allow their executable path to be read.
                }
            }
        }
    }

    private static void AddStandardLocations(IDictionary<string, Candidate> candidates)
    {
        AddCandidate(
            candidates,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HandBrake", ExecutableName),
            isRunning: false,
            isInstalled: true,
            "Program Files");
        AddCandidate(
            candidates,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "HandBrake", ExecutableName),
            isRunning: false,
            isInstalled: true,
            "Program Files (x86)");
    }

    private static void AddRegistryLocations(
        IDictionary<string, Candidate> candidates,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null)
                    {
                        continue;
                    }

                    foreach (var subKeyName in uninstall.GetSubKeyNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var product = uninstall.OpenSubKey(subKeyName);
                        var displayName = product?.GetValue("DisplayName") as string;
                        if (displayName?.Contains("HandBrake", StringComparison.OrdinalIgnoreCase) != true)
                        {
                            continue;
                        }

                        var installLocation = product?.GetValue("InstallLocation") as string;
                        var displayIcon = NormalizeDisplayIcon(product?.GetValue("DisplayIcon") as string);
                        AddCandidate(
                            candidates,
                            string.IsNullOrWhiteSpace(installLocation)
                                ? displayIcon
                                : Path.Combine(installLocation, ExecutableName),
                            isRunning: false,
                            isInstalled: true,
                            "Windows installation record");
                    }
                }
                catch (Exception exception) when (
                    exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    // Registry access is best effort and must not block manual setup.
                }
            }
        }
    }

    private static void AddUserLocations(
        IDictionary<string, Candidate> candidates,
        IEnumerable<string> locations,
        CancellationToken cancellationToken)
    {
        foreach (var location in locations.Where(location => !string.IsNullOrWhiteSpace(location)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(location))
                {
                    if (Path.GetFileName(location).Equals(ExecutableName, StringComparison.OrdinalIgnoreCase))
                    {
                        AddCandidate(candidates, location, false, false, "Selected executable");
                    }

                    continue;
                }

                if (!Directory.Exists(location))
                {
                    continue;
                }

                var directPath = Path.Combine(location, ExecutableName);
                AddCandidate(candidates, directPath, false, false, "Selected folder");

                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    MaxRecursionDepth = 4,
                    MatchCasing = MatchCasing.CaseInsensitive
                };

                foreach (var executable in Directory.EnumerateFiles(location, ExecutableName, options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddCandidate(candidates, executable, false, false, "Selected folder search");
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // A missing or inaccessible user location is shown as no result, not an application failure.
            }
        }
    }

    private static HandBrakeInstallation CreateInstallation(Candidate candidate)
    {
        string? version = null;

        try
        {
            version = FileVersionInfo.GetVersionInfo(candidate.Path).ProductVersion;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Version information is optional for portable or incomplete copies.
        }

        var type = candidate.IsInstalled switch
        {
            true => HandBrakeInstallationType.Installed,
            false => HandBrakeInstallationType.Portable,
            null => ClassifyFromPath(candidate.Path)
        };

        return new HandBrakeInstallation(
            candidate.Path,
            version,
            type,
            Exists: true,
            candidate.IsRunning,
            candidate.Source);
    }

    private static HandBrakeInstallationType ClassifyFromPath(string path)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return IsUnder(path, programFiles) || IsUnder(path, programFilesX86)
            ? HandBrakeInstallationType.Installed
            : HandBrakeInstallationType.Portable;
    }

    private static bool IsUnder(string path, string parent)
    {
        if (string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddCandidate(
        IDictionary<string, Candidate> candidates,
        string? path,
        bool isRunning,
        bool? isInstalled,
        string source)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            path = Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (candidates.TryGetValue(path, out var existing))
        {
            candidates[path] = existing with
            {
                IsRunning = existing.IsRunning || isRunning,
                IsInstalled = existing.IsInstalled == true || isInstalled == true
                    ? true
                    : existing.IsInstalled ?? isInstalled,
                Source = existing.Source.Contains(source, StringComparison.OrdinalIgnoreCase)
                    ? existing.Source
                    : $"{existing.Source}, {source}"
            };
            return;
        }

        candidates[path] = new Candidate(path, isRunning, isInstalled, source);
    }

    private static string? NormalizeDisplayIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Trim('"');
        var commaIndex = normalized.LastIndexOf(',');
        return commaIndex > 0 ? normalized[..commaIndex].Trim().Trim('"') : normalized;
    }

    private sealed record Candidate(string Path, bool IsRunning, bool? IsInstalled, string Source);
}
