using System.Diagnostics;
using System.Security.Cryptography;

namespace CarShow.Services;

// The private server is delivered as a compiled deployment artifact, never source.
public sealed class ClinicLicenseRuntime(IConfiguration config, ILogger<ClinicLicenseRuntime> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue<bool>("ClinicLicensing:Enabled")) return;
        Process? process = null;
        try
        {
            var root = Path.GetFullPath(config["ClinicLicensing:RuntimePath"] ?? "/app/runtime/license-server");
            var data = Path.GetFullPath(config["ClinicLicensing:DataPath"] ?? "/data/licensing");
            if (!OperatingSystem.IsLinux() || !File.Exists(Path.Combine(root, "Api")))
                throw new InvalidOperationException("Compiled Linux license runtime is missing.");
            var mounted = File.ReadLines("/proc/self/mountinfo").Any(line =>
            {
                var path = line.Split(' ')[4].Replace("\\040", " ");
                return path != "/" && (data == path || data.StartsWith(path + "/", StringComparison.Ordinal));
            });
            if (!mounted) throw new InvalidOperationException("License data must be on a persistent mount.");
            Directory.CreateDirectory(data);
            File.SetUnixFileMode(data, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var key = Path.Combine(data, "signing-key.pem");
            var admin = Path.Combine(data, "admin-key");
            var marker = Path.Combine(data, "initialized");
            if ((File.Exists(marker) || Directory.Exists(Path.Combine(data, "records"))) &&
                (!File.Exists(key) || !File.Exists(admin)))
                throw new InvalidOperationException("Existing license keys are missing; restore the backup.");
            if (!File.Exists(key))
            {
                using var rsa = RSA.Create(3072);
                WriteSecret(key, rsa.ExportPkcs8PrivateKeyPem());
            }
            if (!File.Exists(admin)) WriteSecret(admin, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            var start = new ProcessStartInfo(Path.Combine(root, "Api")) { WorkingDirectory = root, UseShellExecute = false };
            // Do not expose CarShow database/JWT secrets to the independent license process.
            start.Environment.Clear();
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:5105";
            start.Environment["DOTNET_PROCESSOR_COUNT"] = "2";
            start.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";
            start.Environment["Server__DataPath"] = Path.Combine(data, "records");
            start.Environment["Server__PrivateKeyPath"] = key;
            start.Environment["Server__AdminKey"] = File.ReadAllText(admin).Trim();
            start.Environment["Server__KeyId"] = "carshow-clinic-1";
            start.Environment["Server__TrustedProxies__0"] = "127.0.0.1";
            start.Environment["Logging__LogLevel__Default"] = "Warning";
            process = Process.Start(start) ?? throw new InvalidOperationException("Cannot launch license server.");
            File.WriteAllText(marker, "carshow-clinic-1\n");
            await process.WaitForExitAsync(stoppingToken);
            logger.LogError("Clinic licensing stopped with exit code {Code}; CarShow remains available.", process.ExitCode);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError("Clinic licensing unavailable: {Error}", exception.Message); }
        finally
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
            process?.Dispose();
        }
    }

    private static void WriteSecret(string path, string value)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        using var stream = new FileStream(path, new FileStreamOptions {
            Mode = FileMode.CreateNew, Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        using var writer = new StreamWriter(stream);
        writer.Write(value);
    }
}
