using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.ComponentModel;
using System.Windows.Shapes;

namespace justsayo_win;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly NotifyIcon _notifyIcon;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;

        viewModel.RequestMinimizeToTray += OnRequestMinimizeToTray;

        _notifyIcon = new NotifyIcon
        {
            Icon = new System.Drawing.Icon("icon.ico"),
            Text = "Just Sayori Launcher",
            Visible = false
        };
        _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

        // create context menu for the tray icon
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Show Launcher", null, (s, e) => ShowWindow());
        contextMenu.Items.Add("Exit", null, (s, e) => System.Windows.Application.Current.Shutdown());
        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is System.Windows.Controls.RadioButton rb)
        {
            if (rb.Content != null)
            {
                vm.Navigate(rb.Content.ToString() ?? "");
            }
        }
    }

    private void OnRequestMinimizeToTray()
    {
        this.Hide();
        _notifyIcon.Visible = true;
    }

    private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
    {
        ShowWindow();
    }

    private void ShowWindow()
    {
        this.Show();
        this.WindowState = WindowState.Normal;
        this.Activate();
        _notifyIcon.Visible = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _notifyIcon.Dispose();
        base.OnClosing(e);
    }
}