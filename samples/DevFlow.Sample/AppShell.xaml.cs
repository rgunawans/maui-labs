#if MACOS
using Microsoft.Maui.Platform.MacOS;
#endif

namespace DevFlow.Sample;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
#if MACOS
        FlyoutBehavior = FlyoutBehavior.Locked;
        MacOSShell.SetUseNativeSidebar(this, true);
#endif
    }
}
