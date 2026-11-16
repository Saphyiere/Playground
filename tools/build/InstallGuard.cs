// The install guard (InstallGuard.targets), run before a build installs an app on a device. On Windows it first stops an install
// whose app file isn't signed with the devices' key, whatever the device holds. Then it stops the build, with a message naming what
// differs, when installing would make .NET for Android uninstall the app, which erases its data, and does nothing more when no
// device is connected or the app isn't on it.
//
// .NET for Android uninstalls an app on the way to installing it:
//   - always, for an app bundle (.aab, a Release build's default): adb refuses `adb uninstall -k`, which would keep the data, so it
//     runs `adb shell cmd package uninstall`, which doesn't (_DeployAppBundle, in Xamarin.Android.Common.targets; dotnet/android#8472);
//   - when Android refuses the update because the app file is signed with another key (INSTALL_FAILED_UPDATE_INCOMPATIBLE) or has
//     a lower versionCode (INSTALL_FAILED_VERSION_DOWNGRADE): fast deployment then uninstalls the app, data and all, and installs
//     again, and no setting turns that off;
//   - when it reinstalls (the device's copy isn't debuggable, or the configuration changed) with AndroidPreserveUserData false.
//
// The device's copy is read with `adb shell dumpsys package`, which gives each certificate the app is signed with as Android's
// Signature.hashCode(): java.util.Arrays.hashCode of the certificate's bytes, in hex. The build's certificate comes from apksigner.
// When the two don't match, the app file on the device is pulled and read with apksigner as well, so a mismatch is certain and the
// message names both keys by their SHA-256.
//
// An inline task (RoslynCodeTaskFactory), compiled by Visual Studio's MSBuild and by the dotnet CLI's alike, so it stays within
// .NET Standard 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class PlaygroundInstallGuard : Task
{
    /// <summary>adb, with its folder.</summary>
    [Required]
    public string Adb { get; set; }

    /// <summary>The device the build installs on, as adb takes it (-s and its serial), or empty for the only one connected.</summary>
    public string AdbTarget { get; set; }

    [Required]
    public string PackageName { get; set; }

    /// <summary>The app's name, for the messages.</summary>
    public string AppName { get; set; }

    /// <summary>apk or aab: what the build installs.</summary>
    public string PackageFormat { get; set; }

    /// <summary>The signed app file the build installs.</summary>
    public string AppFile { get; set; }

    /// <summary>The app's AndroidManifest.xml as built, for its versionCode.</summary>
    public string ManifestFile { get; set; }

    public string Java { get; set; }

    public string ApkSignerJar { get; set; }

    /// <summary>Where the device's app file goes when it's pulled to be read.</summary>
    public string WorkFolder { get; set; }

    /// <summary>The SHA-256 of the devices' key's certificate, so the message can say which key is which.</summary>
    public string DevicesKeySha256 { get; set; }

    public string PreserveUserData { get; set; }

    /// <summary>
    /// "true" to stop an install whose app file isn't signed with the devices' key, whatever the device holds: set on Windows when
    /// the build signs with the devices' key store, not with signing given on its command line.
    /// </summary>
    public string CheckDevicesKey { get; set; }

    /// <summary>The key store the build signed with, named when it isn't the devices' key.</summary>
    public string KeyStore { get; set; }

    /// <summary>"true" (PlaygroundInstallUnchecked) to let one install through where the guard can't check, rather than refuse it.</summary>
    public string Unchecked { get; set; }

    const int TimedOut = -1;
    const int NotStarted = int.MinValue;

    string app;
    string device = "the device";

    public override bool Execute()
    {
        app = string.IsNullOrEmpty(AppName) ? PackageName : AppName;

        // The key first, whatever the device holds. When its file is missing, .NET for Android makes a new key store there, and every
        // build after signs with the new key without a word: a first install on a device without the app would go through, and no
        // release or other build could ever update that copy. So an install from this PC goes ahead only signed with the devices' key.
        List<byte[]> built = null;
        if (string.Equals(CheckDevicesKey, "true", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(DevicesKeySha256) &&
            !string.IsNullOrEmpty(AppFile) && File.Exists(AppFile))
        {
            string keySigner;
            built = Certificates(AppFile, out keySigner);
            if (built.Count == 0)
                return Stop("apksigner couldn't read which key signed " + AppFile + ", so the install guard can't tell whether it's the devices' " +
                    "key. Nothing was installed. apksigner said: " + OneLine(keySigner));
            string devicesKey = DevicesKeySha256.Replace(":", "").Replace(" ", "").ToLowerInvariant();
            if (!built.Select(Sha256).Contains(devicesKey))
                return Stop("This build of " + app + " isn't signed with the devices' key: it's signed with " + Describe(built) +
                    (string.IsNullOrEmpty(KeyStore) ? "" : ", from " + KeyStore) + ", and the devices' key is SHA-256 " + devicesKey + ". " +
                    "A copy signed with another key can't be updated by the releases or by any other build here, and won't install over a copy " +
                    "signed with the devices' key. Nothing was installed. The key store was probably made afresh when its file was missing: " +
                    "put the devices' key back from your backup (tools/release/README.md, One key for every build).");
        }

        // adb's server first, without reading what it writes, so no command below starts it and then waits on it.
        string output;
        if (Run(Adb, "start-server", 60, false, out output) == NotStarted)
            return CantCheck("adb couldn't be started (" + Adb + "), so the install guard can't tell whether installing " + app + " would " +
                "erase its data. Nothing was installed. Check the Android SDK's platform-tools are installed and try again.");

        string state;
        if (Run(Adb, Args("get-state"), 30, true, out state) == TimedOut)
            return CantCheck("adb didn't answer (get-state: " + state + "), so the install guard can't tell whether installing " + app +
                " would erase its data. Nothing was installed. Try again; if it keeps happening, `adb kill-server` first.");
        if (state.IndexOf("more than one device", StringComparison.OrdinalIgnoreCase) >= 0)
            return CantCheck("More than one device is connected, and the install guard can't tell which one " + app + " would be installed on. " +
                "Nothing was installed. Choose one: the device list in Visual Studio, or -p:AdbTarget=\"-s <serial>\" on the command line " +
                "(adb devices lists the serials).");
        // With no device ready the guard can't see the copy it would replace, so the install doesn't go ahead blind.
        if (!Regex.IsMatch(state, @"^device\s*$", RegexOptions.Multiline))
            return CantCheck("No device is ready for " + app + " (" + OneLine(state) + "), so the install guard can't compare it with the copy " +
                "on the device. Nothing was installed. Connect the device, unlock it and allow USB debugging (adb devices should list it as " +
                "device), then try again.");

        string model;
        if (Run(Adb, Args("shell getprop ro.product.model"), 30, true, out model) == 0 && OneLine(model).Length > 0)
            device = "the " + OneLine(model);

        string dump;
        if (Run(Adb, Args("shell dumpsys package " + PackageName), 60, true, out dump) != 0)
            return CantCheck("adb couldn't read the apps on " + device + " (dumpsys package: " + OneLine(dump) + "), so the install guard " +
                "can't tell whether installing " + app + " would erase its data. Nothing was installed. Try again.");
        Match header = Regex.Match(dump, @"^[ \t]*Package \[" + Regex.Escape(PackageName) + @"\]", RegexOptions.Multiline);
        if (!header.Success)
        {
            Log.LogMessage(MessageImportance.Normal, "Install guard: {0} isn't on {1}, so there's nothing to compare.", app, device);
            return true;
        }
        int next = dump.IndexOf("Package [", header.Index + header.Length, StringComparison.Ordinal);
        string entry = next < 0 ? dump.Substring(header.Index) : dump.Substring(header.Index, next - header.Index);

        if (string.Equals(PackageFormat, "aab", StringComparison.OrdinalIgnoreCase))
            return Stop(app + " would be installed on " + device + " as an app bundle (AndroidPackageFormat aab), and before it installs a " +
                "bundle .NET for Android uninstalls the app, which erases its data: adb refuses `adb uninstall -k`, which would keep it, so " +
                "it runs `adb shell cmd package uninstall`. Nothing was installed. Every build here makes an app file (.apk) unless it's " +
                "told otherwise: leave AndroidPackageFormat and AndroidPackageFormats unset, or set them to apk.");

        if (string.Equals(PreserveUserData, "false", StringComparison.OrdinalIgnoreCase))
            return Stop("AndroidPreserveUserData is false, so installing " + app + " on " + device + " could uninstall it first and erase " +
                "its data: .NET for Android reinstalls when the device's copy isn't debuggable or the configuration changed. Nothing was " +
                "installed. Leave AndroidPreserveUserData unset: it's true by default.");

        Match installedVersion = Regex.Match(entry, @"versionCode=(\d+)");
        long builtVersion = BuiltVersionCode();
        if (builtVersion < 0 && !CantCheck("The built AndroidManifest.xml isn't there (" + ManifestFile + "), so the install guard can't " +
                "compare " + app + "'s versionCode with the copy on " + device + ": a lower one would make .NET for Android uninstall the app, " +
                "erasing its data. Nothing was installed. Build it again (a full build, not only the install), then install."))
            return false;
        if (installedVersion.Success && builtVersion >= 0 && long.Parse(installedVersion.Groups[1].Value) > builtVersion)
            return Stop(app + " on " + device + " has versionCode " + installedVersion.Groups[1].Value + ", newer than this build's " +
                builtVersion + ". Android refuses a lower versionCode as an update, and .NET for Android would then uninstall the app, " +
                "erasing its data, to install it. Nothing was installed. Build it with versionCode " + installedVersion.Groups[1].Value +
                " or more (ApplicationVersion).");

        if (string.IsNullOrEmpty(AppFile) || !File.Exists(AppFile))
            return Stop("There's no app file to compare with " + app + " on " + device + " (" + AppFile + "), so the install guard can't " +
                "tell whether installing would erase its data. Nothing was installed.");
        string signer = "";
        if (built == null)
            built = Certificates(AppFile, out signer);
        if (built.Count == 0)
            return Stop("apksigner couldn't read which key signed " + AppFile + ", so the install guard can't tell whether installing it " +
                "would erase " + app + "'s data on " + device + ". Nothing was installed. apksigner said: " + OneLine(signer));

        List<string> installedHashes = SignatureHashes(entry);
        if (installedHashes.Count > 0 && new HashSet<string>(installedHashes).SetEquals(built.Select(AndroidHash)))
            return Same(built);

        // dumpsys gives another key, or gives it in a way this doesn't read: the app file on the device says for certain.
        string why;
        List<byte[]> installed = PulledCertificates(out why);
        if (installed.Count > 0 && new HashSet<string>(installed.Select(Sha256)).SetEquals(built.Select(Sha256)))
            return Same(built);
        if (installed.Count == 0 && installedHashes.Count == 0)
            return Stop("The install guard couldn't read which key signed " + app + " on " + device + " (" + why + "), so it can't tell " +
                "whether installing would erase its data. Nothing was installed.");

        string onDevice = installed.Count > 0
            ? Describe(installed)
            : "a certificate with hash " + string.Join(", ", installedHashes) + " in dumpsys package (its app file couldn't be read: " + why + ")";
        return Stop(app + " on " + device + " is signed with a different key than this build. Android would refuse this build as an " +
            "update, and .NET for Android would then uninstall the app, erasing its data, to install it. Nothing was installed." +
            Environment.NewLine + "  On " + device + ": " + onDevice +
            Environment.NewLine + "  This build: " + Describe(built) +
            Environment.NewLine + "To keep its data, build with the key the device's copy has: every build here is signed with the devices' " +
            "key unless its command line gives other signing. To replace the app anyway, back up its data, uninstall it from the device " +
            "yourself, and install again.");
    }

    bool Same(List<byte[]> built)
    {
        Log.LogMessage(MessageImportance.High, "Install guard: {0} on {1} is signed with the same key as this build ({2}), so installing it " +
            "keeps its data.", app, device, Describe(built));
        return true;
    }

    bool Stop(string message)
    {
        Log.LogError(message);
        return false;
    }

    // Where the guard can't check, it refuses, saying what to do, unless the build is told once to go ahead without the check: then
    // it goes ahead, saying so. true to go on with the install.
    bool CantCheck(string message)
    {
        if (string.Equals(Unchecked, "true", StringComparison.OrdinalIgnoreCase))
        {
            Log.LogWarning(message.Replace(" Nothing was installed.", "") + " Installing anyway, without the guard's check, as " +
                "PlaygroundInstallUnchecked asks.");
            return true;
        }
        return Stop(message + " To install anyway, once, without this check: -p:PlaygroundInstallUnchecked=true.");
    }

    string Args(string command)
    {
        return string.IsNullOrWhiteSpace(AdbTarget) ? command : AdbTarget.Trim() + " " + command;
    }

    /// <summary>The versionCode in the built AndroidManifest.xml, or -1.</summary>
    long BuiltVersionCode()
    {
        if (string.IsNullOrEmpty(ManifestFile) || !File.Exists(ManifestFile))
            return -1;
        Match code = Regex.Match(File.ReadAllText(ManifestFile), @"android:versionCode\s*=\s*""(\d+)""");
        return code.Success ? long.Parse(code.Groups[1].Value) : -1;
    }

    /// <summary>The certificates an app file is signed with, as apksigner reads them.</summary>
    List<byte[]> Certificates(string appFile, out string output)
    {
        var certificates = new List<byte[]>();
        if (Run(Java, "-jar " + Quote(ApkSignerJar) + " verify --print-certs-pem " + Quote(appFile), 120, true, out output) != 0)
            return certificates;
        foreach (Match pem in Regex.Matches(output, "-----BEGIN CERTIFICATE-----(.+?)-----END CERTIFICATE-----", RegexOptions.Singleline))
            certificates.Add(Convert.FromBase64String(Regex.Replace(pem.Groups[1].Value, @"\s+", "")));
        return certificates;
    }

    /// <summary>The certificates the device's copy is signed with, from its app file, pulled from the device.</summary>
    List<byte[]> PulledCertificates(out string why)
    {
        why = "";
        var none = new List<byte[]>();
        string paths;
        if (Run(Adb, Args("shell pm path " + PackageName), 60, true, out paths) != 0)
        {
            why = "pm path: " + OneLine(paths);
            return none;
        }
        Match path = Regex.Match(paths, @"^package:(\S*/base\.apk)\s*$", RegexOptions.Multiline);
        if (!path.Success)
            path = Regex.Match(paths, @"^package:(\S+)", RegexOptions.Multiline);
        if (!path.Success)
        {
            why = "pm path: " + OneLine(paths);
            return none;
        }
        string local = Path.Combine(string.IsNullOrEmpty(WorkFolder) ? Path.GetTempPath() : WorkFolder, "installed-" + PackageName + ".apk");
        try
        {
            string pulled;
            if (Run(Adb, Args("pull " + Quote(path.Groups[1].Value) + " " + Quote(local)), 600, true, out pulled) != 0 || !File.Exists(local))
            {
                why = "adb pull: " + OneLine(pulled);
                return none;
            }
            string signer;
            List<byte[]> certificates = Certificates(local, out signer);
            if (certificates.Count == 0)
                why = "apksigner: " + OneLine(signer);
            return certificates;
        }
        finally
        {
            try { File.Delete(local); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The certificate hashes in an app's dumpsys package entry: "signatures:[1a2b3c4d]" since Android 9, "PackageSignatures{… [1a2b3c4d]}" before.</summary>
    static List<string> SignatureHashes(string entry)
    {
        Match list = Regex.Match(entry, @"(?<!past )signatures:\[([0-9a-fA-F, ]*)\]");
        if (!list.Success)
            list = Regex.Match(entry, @"PackageSignatures\{[0-9a-fA-F]+ \[([0-9a-fA-F, ]*)\]\}");
        return list.Success
            ? list.Groups[1].Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(hash => hash.ToLowerInvariant()).ToList()
            : new List<string>();
    }

    /// <summary>Android's Signature.hashCode() of a certificate: java.util.Arrays.hashCode of its bytes (Java's bytes are signed), in hex as Integer.toHexString writes it.</summary>
    static string AndroidHash(byte[] certificate)
    {
        int hash = 1;
        unchecked
        {
            foreach (byte b in certificate)
                hash = 31 * hash + (sbyte)b;
        }
        return ((uint)hash).ToString("x");
    }

    static string Sha256(byte[] certificate)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(certificate)).Replace("-", "").ToLowerInvariant();
    }

    string Describe(IEnumerable<byte[]> certificates)
    {
        string devicesKey = (DevicesKeySha256 ?? "").Replace(":", "").Replace(" ", "").ToLowerInvariant();
        return string.Join("; ", certificates.Select(Sha256).Distinct().Select(sha =>
            "SHA-256 " + sha + (sha == devicesKey ? ", the devices' key" : "")));
    }

    static string Quote(string path)
    {
        return "\"" + path + "\"";
    }

    static string OneLine(string text)
    {
        return Regex.Replace((text ?? "").Trim(), @"\s*[\r\n]+\s*", " / ");
    }

    /// <summary>Runs a tool and returns its exit code, TimedOut or NotStarted, with what it wrote (out and error) when capture is set.</summary>
    int Run(string file, string arguments, int seconds, bool capture, out string output)
    {
        output = "";
        Log.LogMessage(MessageImportance.Low, "Install guard: {0} {1}", file, arguments);
        var start = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture,
        };
        try
        {
            using (Process process = Process.Start(start))
            {
                System.Threading.Tasks.Task<string> standardOutput = capture ? process.StandardOutput.ReadToEndAsync() : null;
                System.Threading.Tasks.Task<string> standardError = capture ? process.StandardError.ReadToEndAsync() : null;
                if (!process.WaitForExit(seconds * 1000))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                    output = "it didn't finish in " + seconds + " seconds";
                    return TimedOut;
                }
                if (capture)
                {
                    // What it wrote ends with it, unless something it started (adb's server) keeps the output open: a moment, then on.
                    try { System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { standardOutput, standardError }, 5000); }
                    catch (AggregateException) { }
                    output = Text(standardOutput) + Text(standardError);
                }
                Log.LogMessage(MessageImportance.Low, "Install guard: exit code {0}: {1}", process.ExitCode, output);
                return process.ExitCode;
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is FileNotFoundException)
        {
            output = e.Message;
            return NotStarted;
        }
    }

    static string Text(System.Threading.Tasks.Task<string> read)
    {
        return read.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? read.Result : "";
    }
}
