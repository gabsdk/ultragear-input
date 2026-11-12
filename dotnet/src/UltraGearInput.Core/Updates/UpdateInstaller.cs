using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltraGearInput.Core.Updates;

public sealed record UpdatePlan(string Target, string Payload, string Sha256, int ParentId, long ParentStarted, string Version, bool Restart = true);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UpdatePlan))]
public sealed partial class UpdatePlanJsonContext : JsonSerializerContext;

public static class UpdateInstaller
{
    public const string ApplyFlag = "--apply-update";
    public const string UpdatedFlag = "--updated";
    public const string FailedFlag = "--update-failed";

    public static string CurrentVersion => typeof(UpdateInstaller).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    public static UpdateTarget CurrentTarget => OperatingSystem.IsWindows() ? UpdateTarget.WindowsX64
        : RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => UpdateTarget.LinuxX64,
            Architecture.Arm64 => UpdateTarget.LinuxArm64,
            _ => throw new UpdateException("unsupported"),
        };

    public static async Task PrepareAsync(UpdateClient client, UpdateRelease release, string executable, CancellationToken cancellationToken)
    {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "UltraGearInput.Core.dll")))
        {
            throw new UpdateException("unsupported");
        }
        var target = new FileInfo(executable).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? Path.GetFullPath(executable);
        var stage = Directory.CreateTempSubdirectory("ultragear-update-").FullName;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var handedOff = false;
        try
        {
            var probe = Path.Combine(Path.GetDirectoryName(target)!, ".ultragear-write-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (File.Create(probe)) { }
                File.Delete(probe);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new UpdateException("permission");
            }
            var archive = Path.Combine(stage, release.Package.Name);
            await client.DownloadAsync(release, archive, cancellationToken).ConfigureAwait(false);
            var payload = Path.Combine(stage, "payload");
            await ExtractAsync(archive, payload, release, CurrentTarget, cancellationToken).ConfigureAwait(false);
            var helper = Path.Combine(stage, OperatingSystem.IsWindows() ? "helper.exe" : "helper");
            File.Copy(target, helper);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            using var parent = Process.GetCurrentProcess();
            var plan = new UpdatePlan(target, payload, await HashAsync(payload, cancellationToken).ConfigureAwait(false),
                parent.Id, parent.StartTime.ToUniversalTime().Ticks, release.Version.ToString());
            var planFile = Path.Combine(stage, "plan.json");
            await File.WriteAllTextAsync(planFile, JsonSerializer.Serialize(plan, UpdatePlanJsonContext.Default.UpdatePlan), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, WorkingDirectory = stage, CreateNoWindow = true };
            start.ArgumentList.Add(ApplyFlag);
            start.ArgumentList.Add(planFile);
            using var child = Process.Start(start) ?? throw new UpdateException("installFailed");
            handedOff = true;
        }
        finally
        {
            if (!handedOff)
            {
                Directory.Delete(stage, recursive: true);
            }
        }
    }

    public static async Task ExtractAsync(string archive, string destination, UpdateRelease release, UpdateTarget target, CancellationToken cancellationToken)
    {
        if (target == UpdateTarget.WindowsX64)
        {
            File.Copy(archive, destination);
        }
        else
        {
            await using var input = File.OpenRead(archive);
            await using var gzip = new GZipStream(input, CompressionMode.Decompress);
            await using var reader = new TarReader(gzip);
            var expected = release.Package.Name[..^7] + "/ultragear-input";
            var found = false;
            var entries = 0;
            long expandedSize = 0;
            while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
            {
                expandedSize += entry.Length;
                if (++entries > 10000 || entry.Length < 0 || expandedSize > 512L * 1024 * 1024)
                {
                    throw new UpdateException("package");
                }
                if (entry.Name != expected)
                {
                    continue;
                }
                if (found || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                    || entry.Length is <= 0 or > UpdateClient.MaximumPackageSize || entry.DataStream is null)
                {
                    throw new UpdateException("package");
                }
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await entry.DataStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                found = true;
            }
            if (!found)
            {
                throw new UpdateException("package");
            }
        }
        await using var binary = File.OpenRead(destination);
        var header = new byte[64];
        if (await binary.ReadAsync(header, cancellationToken).ConfigureAwait(false) != header.Length
            || (target == UpdateTarget.WindowsX64 ? header[0] != 'M' || header[1] != 'Z'
                : !header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x7F, 0x45, 0x4C, 0x46 })
                    || header[4] != 2 || header[5] != 1 || header[18] != (target == UpdateTarget.LinuxArm64 ? 183 : 62) || header[19] != 0))
        {
            throw new UpdateException("package");
        }
    }

    public static async Task<int> ApplyAsync(string planFile, CancellationToken cancellationToken)
    {
        UpdatePlan? plan = null;
        var validated = false;
        var phase = "plan validation";
        try
        {
            var stage = Path.GetDirectoryName(Path.GetFullPath(planFile))!;
            var helper = Path.GetFullPath(Environment.ProcessPath!);
            if (Path.GetDirectoryName(helper) != stage || Path.GetFileName(planFile) != "plan.json")
            {
                throw new UpdateException("package");
            }
            plan = JsonSerializer.Deserialize(await File.ReadAllTextAsync(planFile, cancellationToken).ConfigureAwait(false), UpdatePlanJsonContext.Default.UpdatePlan)
                ?? throw new UpdateException("package");
            if (plan.Payload != Path.Combine(stage, "payload") || !Path.IsPathFullyQualified(plan.Target) || plan.ParentId <= 0)
            {
                throw new UpdateException("package");
            }
            _ = ReleaseVersion.Parse(plan.Version);
            validated = true;
            phase = "parent exit";
            Process? parent = null;
            try { parent = Process.GetProcessById(plan.ParentId); }
            catch (ArgumentException) { }
            if (parent is not null)
            {
                using (parent)
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStarted)
                    {
                        throw new UpdateException("installFailed");
                    }
                    await parent.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                }
            }
            phase = "replacement";
            await ReplaceAsync(plan, cancellationToken, async () =>
            {
                await VerifyVersionAsync(plan.Target, plan.Version, cancellationToken).ConfigureAwait(false);
                if (plan.Restart)
                {
                    Start(plan.Target, UpdatedFlag, plan.Version, stage);
                }
            }).ConfigureAwait(false);
            phase = "cleanup";
            if (!plan.Restart)
            {
                File.Delete(plan.Payload);
                File.Delete(planFile);
            }
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UpdateException or JsonException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            if (validated && plan?.Restart == true && File.Exists(plan.Target))
            {
                Start(plan.Target, FailedFlag, "installFailed", Path.GetDirectoryName(planFile));
            }
            await Console.Error.WriteLineAsync($"Update helper failed during {phase}: {exception.GetType().Name}: {exception.Message}").ConfigureAwait(false);
            await Console.Error.WriteLineAsync(UpdateText.Get("installFailed")).ConfigureAwait(false);
            return 1;
        }
    }

    public static async Task ReplaceAsync(UpdatePlan plan, CancellationToken cancellationToken, Func<Task>? restart = null)
    {
        if (await HashAsync(plan.Payload, cancellationToken).ConfigureAwait(false) != plan.Sha256)
        {
            throw new UpdateException("checksum");
        }
        var replacement = plan.Target + ".new-" + Guid.NewGuid().ToString("N");
        var backup = plan.Target + ".previous-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(plan.Payload, replacement);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(replacement, File.GetUnixFileMode(plan.Target));
            }
            File.Move(plan.Target, backup);
            try
            {
                File.Move(replacement, plan.Target);
                if (restart is not null)
                {
                    await restart().ConfigureAwait(false);
                }
            }
            catch
            {
                File.Move(backup, plan.Target, overwrite: true);
                throw;
            }
            File.Delete(backup);
        }
        finally
        {
            File.Delete(replacement);
        }
    }

    public static async Task<string> HashAsync(string file, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static async Task VerifyVersionAsync(string executable, string version, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("--version");
        using var child = Process.Start(start) ?? throw new UpdateException("installFailed");
        var output = child.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = child.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await child.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            if (child.ExitCode != 0 || (await output.ConfigureAwait(false)).Trim() != version)
            {
                await Console.Error.WriteLineAsync($"Update version probe failed: exit={child.ExitCode}, stdout={(await output.ConfigureAwait(false)).Trim()}, stderr={(await error.ConfigureAwait(false)).Trim()}").ConfigureAwait(false);
                throw new UpdateException("package");
            }
            await error.ConfigureAwait(false);
        }
        catch
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
    }

    public static async Task CleanupAsync(string stage, int helperId)
    {
        try
        {
            var fullPath = Path.GetFullPath(stage);
            if (Path.GetDirectoryName(fullPath) != Path.TrimEndingDirectorySeparator(Path.GetTempPath())
                || !Path.GetFileName(fullPath).StartsWith("ultragear-update-", StringComparison.Ordinal)
                || new DirectoryInfo(fullPath).LinkTarget is not null)
            {
                return;
            }
            var planFile = Path.Combine(fullPath, "plan.json");
            var plan = JsonSerializer.Deserialize(await File.ReadAllTextAsync(planFile).ConfigureAwait(false), UpdatePlanJsonContext.Default.UpdatePlan);
            if (plan is null || plan.Target != Environment.ProcessPath || plan.Payload != Path.Combine(fullPath, "payload"))
            {
                return;
            }
            try
            {
                using var helper = Process.GetProcessById(helperId);
                await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            catch (ArgumentException) { }
            Directory.Delete(fullPath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or TimeoutException or System.ComponentModel.Win32Exception) { }
    }

    private static void Start(string executable, string flag, string value, string? stage = null)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)!, CreateNoWindow = true };
        start.ArgumentList.Add(flag);
        start.ArgumentList.Add(value);
        if (stage is not null)
        {
            start.ArgumentList.Add(stage);
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        using var child = Process.Start(start) ?? throw new UpdateException("installFailed");
    }
}
