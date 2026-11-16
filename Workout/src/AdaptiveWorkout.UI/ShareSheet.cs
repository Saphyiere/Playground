namespace AdaptiveWorkout.UI;

/// <summary>
/// Sends a file on from the app: the phone's share sheet, for an export of everything (Settings › Your data), to Drive or to the
/// PC. The phone app registers its own; other hosts, like the preview, can't, and say where the file is instead.
/// </summary>
public class ShareSheet
{
    /// <summary>Offers a file to be saved or sent elsewhere, as it is; false where the host can't.</summary>
    public virtual Task<bool> OfferAsync(string path, string title) => Task.FromResult(false);
}
