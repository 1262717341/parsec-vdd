using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Navigation;

namespace ParsecVDisplay
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            xAppName.Content = $"{App.TITLE} v{App.VERSION}";

            // prevent frame history
            xFrame.Navigating += (_, e) => { e.Cancel = e.NavigationMode != NavigationMode.New; };
            xFrame.Navigated += (_, e) => { xFrame.NavigationService.RemoveBackEntry(); };

            xDisplays.Children.Clear();
            xNoDisplay.Visibility = Visibility.Hidden;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            Shadow.ApplyShadow(hwnd);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;

            if (xFrame.Content != null)
            {
                xFrame.Visibility = Visibility.Hidden;
                xFrame.Content = null;
                xDisplays.Visibility = Visibility.Visible;
                xButtons.Visibility = Visibility.Visible;
            }
            else
            {
                this.Hide();
            }
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= Window_Loaded;

            if (App.Silent)
                Hide();

            ContextMenu.DataContext = this;

            // 初始化菜单勾选状态（从注册表读取真实值）
            xRunOnStartup.IsChecked = Config.RunOnStartup;
            xFallbackDisplay.IsChecked = Config.FallbackDisplay;
            xKeepScreenOn.IsChecked = Config.KeepScreenOn;

            Tray.Init(this, ContextMenu);
            ContextMenu = null;

            ParsecVDD.DisplayChanged += DisplayChanged;
            ParsecVDD.Invalidate();
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
        {
            ParsecVDD.DisplayChanged -= DisplayChanged;
            Tray.Uninit();
        }

        private void DisplayChanged(List<Display> displays, bool noMonitors)
        {
            xDisplays.Children.Clear();
            xNoDisplay.Visibility = displays.Count <= 0 ? Visibility.Visible : Visibility.Hidden;

            foreach (var display in displays)
            {
                var item = new Components.DisplayItem(display);
                xDisplays.Children.Add(item);
            }

            xAdd.IsEnabled = true;

            if (noMonitors && Config.FallbackDisplay)
            {
                AddDisplay(null, EventArgs.Empty);
            }
        }

        private void AddDisplay(object sender, EventArgs e)
        {
            if (ParsecVDD.DisplayCount >= ParsecVDD.MAX_DISPLAYS)
            {
                MessageBox.Show(this,
                    $"无法添加更多虚拟显示器，已达到最大数量（{ParsecVDD.MAX_DISPLAYS} 个）。",
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                ParsecVDD.AddDisplay();
                xAdd.IsEnabled = false;
            }
        }

        private void RemoveLastDisplay(object sender, EventArgs e)
        {
            xAdd.IsEnabled = false;
            ParsecVDD.RemoveLastDisplay();
        }

        private void OpenCustom(object sender, EventArgs e)
        {
            xDisplays.Visibility = Visibility.Hidden;
            xButtons.Visibility = Visibility.Hidden;
            xFrame.Content = new Components.CustomPage();
            xFrame.Visibility = Visibility.Visible;
        }

        private void OpenSettings(object sender, EventArgs e)
        {
            Helper.ShellExec("ms-settings:display");
        }

        private void SyncSettings(object sender, EventArgs e)
        {
            xAdd.IsEnabled = false;
            xDisplays.Children.Clear();

            ParsecVDD.Invalidate();
        }

        private void ToggleRunOnStartup(object sender, RoutedEventArgs e)
        {
            bool enable = xRunOnStartup.IsChecked;

            Config.RunOnStartup = enable;

            // 回读真实状态，写入失败时菜单不会显示为已勾选
            xRunOnStartup.IsChecked = Config.RunOnStartup;

            if (enable && !xRunOnStartup.IsChecked)
            {
                MessageBox.Show(this,
                    "无法写入开机启动项，请检查是否被杀毒软件拦截。",
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ToggleFallbackDisplay(object sender, RoutedEventArgs e)
        {
            Config.FallbackDisplay = xFallbackDisplay.IsChecked;
            xFallbackDisplay.IsChecked = Config.FallbackDisplay;
        }

        private void ToggleKeepScreenOn(object sender, RoutedEventArgs e)
        {
            Config.KeepScreenOn = xKeepScreenOn.IsChecked;
            xKeepScreenOn.IsChecked = Config.KeepScreenOn;
        }

        private void QueryStatus(object sender, EventArgs e)
        {
            if (e is MouseEventArgs mbe)
                mbe.Handled = true;

            Tray.ShowApp();

            var status = ParsecVDD.QueryStatus();
            var version = ParsecVDD.QueryVersion();

            MessageBox.Show(this,
                $"Parsec 虚拟显示器驱动版本：v{version}\n驱动状态：{Device.StatusText(status)}",
                App.TITLE, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExitApp(object sender, EventArgs e)
        {
            if (ParsecVDD.DisplayCount > 0)
            {
                var message = ParsecVDD.ServiceManaged
                    ? "退出主程序后，虚拟显示器仍由后台服务保持（串流可继续使用）。\n要彻底拔除显示器，请在界面里移除。\n确定要退出吗？"
                    : "退出后，已添加的虚拟显示器都会被拔除。\n（下次启动时会按本次保存的分辨率自动恢复）\n确定要退出吗？";

                if (MessageBox.Show(this, message, App.TITLE,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }

            Tray.Uninit();
            Application.Current.Shutdown();
        }

        private void OpenRepoLink(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            Helper.OpenLink("https://github.com/nomi-san/parsec-vdd");
        }
    }
}
