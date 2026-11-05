using Prima.UI.Looks;

namespace Prima.Tablet;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        ShowLook(TabletLookSetting.Last);
    }

    /// <summary>The look's ground behind the page and in its web view, seen while the page loads.</summary>
    public void ShowLook(Look look)
    {
        var ground = Color.FromArgb(look.Ground);
        BackgroundColor = ground;
        blazorWebView.BackgroundColor = ground;
    }
}
