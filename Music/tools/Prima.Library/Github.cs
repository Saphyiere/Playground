using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;

namespace Prima.Library;

/// <summary>
/// Public GitHub repositories, kept in the cache as shallow clones: a second run uses what's there (delete a folder to fetch it
/// again). Without git on the PATH, the repository comes as a zip from codeload.github.com instead.
/// </summary>
internal static class Github
{
    public static async Task<string> Fetch(HttpClient http, string cache, string owner, string repo)
    {
        var folder = Path.Combine(cache, "github", $"{owner}-{repo}");
        if (Directory.Exists(folder)) return folder;

        // Fetched beside, then moved in whole, so a run that stops halfway leaves nothing that looks finished.
        var part = folder + ".part";
        if (Directory.Exists(part)) Directory.Delete(part, true);
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
        Console.WriteLine($"  {owner}/{repo}…");
        try
        {
            var start = new ProcessStartInfo("git", ["clone", "--quiet", "--depth", "1", $"https://github.com/{owner}/{repo}.git", part]) { RedirectStandardError = true };
            start.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";
            using var git = Process.Start(start)!;
            var errors = git.StandardError.ReadToEndAsync();
            await git.WaitForExitAsync();
            if (git.ExitCode != 0) throw new InvalidOperationException($"git couldn't clone {owner}/{repo}: {await errors}");
        }
        catch (Win32Exception)
        {
            var zip = part + ".zip";
            await Pdmx.Download(http, $"https://codeload.github.com/{owner}/{repo}/zip/HEAD", zip);
            var unzipped = part + ".unzipped";
            ZipFile.ExtractToDirectory(zip, unzipped, overwriteFiles: true);
            Directory.Move(Directory.GetDirectories(unzipped).Single(), part);
            Directory.Delete(unzipped, true);
            File.Delete(zip);
        }
        Directory.Move(part, folder);
        return folder;
    }
}
