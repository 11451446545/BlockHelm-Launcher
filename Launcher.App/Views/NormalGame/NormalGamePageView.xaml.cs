using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views.NormalGame;

public partial class NormalGamePageView : UserControl
{
    public NormalGamePageView()
    {
        InitializeComponent();
    }

    public FrameworkElement RootElement => PageRoot;
}
