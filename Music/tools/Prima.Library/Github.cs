using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;

namespace Prima.Library;

/// <summary>
/// Public GitHub repositories, kept in the cache as plain folders holding only what a source reads: the repository's top-level
/// files and the one folder asked for (kern, MS3). A shallow, sparse git clone downloads just those, and its .git is then taken
/// away; without git, the repository's zip is unpacked the same way. A folder is whole once it holds <c>.fetched</c>: a second
/// run uses it as it is (delete it to fetch again), and one a stopped run left half-done is fetched again.
/// </summary>
internal static class Github
{
    public static async Task<string> Fetch(HttpClient http, string cache, string owner, string repo, string keep)
    {
        var folder = Path.Combine(cache, "github", $"{owner}-{repo}");
        var whole = Path.Combine(folder, ".fetched");
        if (File.Exists(whole)) return folder;

        // A half-done fetch goes. So does the .part folder this tool once cloned into before moving it into place, which nothing
        // uses now, so one that can't be deleted yet is only mentioned.
        Remove(folder);
        try
        {
            Remove(folder + ".part");
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine($"  {e.Message}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
        Console.WriteLine($"  {owner}/{repo}…");
        try
        {
            // Git keeps line endings as they are (core.autocrlf, which on Windows would otherwise turn them into CRLF).
            await Git(Path.GetDirectoryName(folder)!, "clone", "--quiet", "--depth", "1", "--filter=blob:none", "--sparse", "-c", "core.autocrlf=false",
                $"https://github.com/{owner}/{repo}.git", folder);
            await Git(folder, "sparse-checkout", "set", keep);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Console.WriteLine($"  {(e is Win32Exception ? "No git on the PATH" : e.Message)}: downloading the repository's zip instead.");
            Remove(folder);
            var zip = folder + ".zip";
            await Pdmx.Download(http, $"https://codeload.github.com/{owner}/{repo}/zip/HEAD", zip);
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    // Inside the zip's one top folder: the files beside it, and the folder kept.
                    var path = entry.FullName[(entry.FullName.IndexOf('/') + 1)..];
                    if (path.Length == 0 || path.EndsWith('/') || path.Contains('/') && !path.StartsWith(keep + "/", StringComparison.Ordinal)) continue;

                    var target = Path.Combine(folder, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
            File.Delete(zip);
        }

        // Without its .git the folder is plain files: no read-only packs (which Windows won't delete without clearing them first),
        // and no repository inside the repo for Visual Studio to find and hold open. Left where it can't be deleted; it does no harm.
        try
        {
            Remove(Path.Combine(folder, ".git"));
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine($"  Kept {owner}/{repo}'s .git: {e.Message}");
        }
        await File.WriteAllTextAsync(whole, $"{owner}/{repo}: the top-level files and {keep}, fetched {DateTime.UtcNow:yyyy-MM-dd}.");
        return folder;

        // Runs git in a folder; Process.Start throws a Win32Exception when git isn't installed.
        static async Task Git(string where, params string[] arguments)
        {
            using var git = Process.Start(new ProcessStartInfo("git", arguments) { WorkingDirectory = where, RedirectStandardError = true })!;
            var errors = git.StandardError.ReadToEndAsync();
            await git.WaitForExitAsync();
            if (git.ExitCode != 0) throw new InvalidOperationException($"git {arguments[0]} failed: {(await errors).Trim()}");
        }
    }

    // Deletes a folder that may hold read-only files (git's packs), trying again for a few seconds while something (a virus
    // scanner, Visual Studio) still has a new file in it open.
    private static void Remove(string folder)
    {
        for (var attempt = 1; Directory.Exists(folder); attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(folder, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == 6) throw new InvalidOperationException($"Couldn't delete {folder} ({e.Message}). Close whatever has it open, or delete it yourself, then run this again.", e);
                Thread.Sleep(500 * attempt);
            }
        }
    }
}
