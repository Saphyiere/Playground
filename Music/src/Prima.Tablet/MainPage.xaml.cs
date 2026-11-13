using Microsoft.AspNetCore.Components;
using Prima.UI.Looks;

namespace Prima.Tablet;

public partial class MainPage : ContentPage
{
    private bool openingConversion;

    public MainPage()
    {
        InitializeComponent();
        if (ConversionNavigation.PendingPath is { } path)
        {
            blazorWebView.StartPath = path;
            ConversionNavigation.Consumed(path);
        }
        ShowLook(TabletLookSetting.Last);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ConversionNavigation.Requested += OpenConversion;
        OpenConversion();
    }

    protected override void OnDisappearing()
    {
        ConversionNavigation.Requested -= OpenConversion;
        base.OnDisappearing();
    }

    private async void OpenConversion()
    {
        if (openingConversion) return;
        openingConversion = true;
        try
        {
            // The Activity can receive an intent while Blazor is still starting. Keep the request until dispatch succeeds.
            for (var attempt = 0; attempt < 50 && ConversionNavigation.PendingPath is { } path; attempt++)
            {
                if (await blazorWebView.TryDispatchAsync(services => services.GetRequiredService<NavigationManager>().NavigateTo(path))) ConversionNavigation.Consumed(path);
                else await Task.Delay(100);
            }
        }
        catch (Exception error) { Android.Util.Log.Warn("PrimaConversion", "Could not open saved conversion: " + error.Message); }
        finally { openingConversion = false; }
    }

    /// <summary>The look's ground behind the page and in its web view, seen while the page loads.</summary>
    public void ShowLook(Look look)
    {
        var ground = Color.FromArgb(look.Ground);
        BackgroundColor = ground;
        blazorWebView.BackgroundColor = ground;
    }
}
