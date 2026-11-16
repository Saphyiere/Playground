using AdaptiveWorkout.UI;

namespace AdaptiveWorkout.Phone;

/// <summary>The phone's share sheet, for an export of everything (Settings › Your data): to Drive, to the PC, wherever it's sent.</summary>
public sealed class PhoneShareSheet : ShareSheet
{
    public override async Task<bool> OfferAsync(string path, string title)
    {
        // An export: a whole file in the cache already, named for what it is ("Workout 2026-11-16 1530.zip").
        await Share.Default.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path) });
        return true;
    }
}
