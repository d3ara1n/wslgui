using Huskui.Avalonia.Controls;
using Avalonia.Controls;

namespace WslGui.Views;

public partial class MainWindow : AppWindow
{
    private bool allowClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void AllowClose()
    {
        allowClose = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }
}
