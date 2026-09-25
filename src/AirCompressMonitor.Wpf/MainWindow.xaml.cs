using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AirCompressMonitor.Wpf.ViewModel;

namespace AirCompressMonitor.Wpf
{
    /// <summary>
    /// 主窗口。这里只做「界面自己的事」：动画、控件联动、把界面输入转成一次 VM 调用。
    /// 采集、解析、诊断、落盘一概不碰 —— 那是 Comm / Storage 两层的职责。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            _vm.Tip += ShowAlarmTip;        // VM 通过事件提示，不反向抓窗口
            DataContext = _vm;
        }

        // =====================================================================
        // 生命周期
        // =====================================================================
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshSerialPorts();
            ConfigPanel.Visibility = Visibility.Collapsed;
            if (ConfigPanel.RenderTransform == null)
            {
                ConfigPanel.RenderTransform = new TranslateTransform { Y = 60 };
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _vm.Dispose();      // 停采集、断链路、刷盘、释放存储
        }

        private void Button_Exit(object sender, RoutedEventArgs e)
        {
            Close();            // 统一走 Window_Closing，避免两条退出路径行为不一致
        }

        private void Button_Minimize(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();     // 无边框窗口要自己实现拖动
            }
        }

        // =====================================================================
        // 顶部提示
        // =====================================================================
        public void ShowAlarmTip(string msg)
        {
            Dispatcher.Invoke(new Action(() =>
            {
                AlarmTip.Text = msg;
                AlarmTip.Visibility = Visibility.Visible;
            }));

            // 每次提示重新计时：连着来两条时，前一条的收起定时器不能把后一条也收掉。
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                AlarmTip.Visibility = Visibility.Collapsed;
            };
            timer.Start();
        }

        // =====================================================================
        // 串口 / 链路配置
        // =====================================================================
        private void RefreshSerialPorts()
        {
            var ports = MainViewModel.AvailablePorts();
            comPortName.ItemsSource = ports;
            if (ports.Length > 0)
            {
                comPortName.SelectedIndex = 0;
            }
        }

        private void butRefreshPort_Click(object sender, RoutedEventArgs e)
        {
            RefreshSerialPorts();
        }

        private void butConfig_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.IsConfigExpanded)
            {
                _vm.IsConfigExpanded = false;
                CloseConfigPanel();
            }
            else
            {
                _vm.IsConfigExpanded = true;
                RefreshSerialPorts();
                OpenConfigPanel();
            }
        }

        private void butConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.IsConnected)
            {
                _vm.Disconnect();
                ShowAlarmTip("已断开设备");
                return;
            }

            // 界面上的选择先同步回 VM，再发起连接
            _vm.PortName = comPortName.Text;
            _vm.BaudRate = comBaudRate.Text;
            _vm.SlaveIdText = txtSlaveId.Text;
            _vm.SlaveName = txtSlaveName.Text;
            _vm.TcpHost = txtTcpHost.Text;
            _vm.TcpPort = txtTcpPort.Text;

            if (_vm.Connect())
            {
                CloseConfigPanel();
            }
        }

        // =====================================================================
        // 配置面板动画
        // =====================================================================
        private bool _isAnimating;

        private void OpenConfigPanel()
        {
            if (_isAnimating) return;
            if (ConfigPanel.Visibility == Visibility.Visible) return;

            _isAnimating = true;
            ConfigPanel.Visibility = Visibility.Visible;
            ConfigPanel.UpdateLayout();     // 先量高度，才能从「面板高度」滑到 0
            double panelHeight = Math.Max(ConfigPanel.ActualHeight, 1);

            var slideIn = new DoubleAnimation
            {
                From = panelHeight,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.25),
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
            };
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.2)
            };
            slideIn.Completed += (s, args) => { _isAnimating = false; };

            var transform = ConfigPanel.RenderTransform as TranslateTransform;
            if (transform != null)
            {
                transform.BeginAnimation(TranslateTransform.YProperty, slideIn);
            }
            else
            {
                _isAnimating = false;
            }
            ConfigPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void CloseConfigPanel()
        {
            if (_isAnimating) return;
            if (ConfigPanel.Visibility != Visibility.Visible) return;

            _isAnimating = true;
            double panelHeight = Math.Max(ConfigPanel.ActualHeight, 1);

            var slideOut = new DoubleAnimation
            {
                From = 0,
                To = panelHeight,
                Duration = TimeSpan.FromSeconds(0.25),
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
            };
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.2)
            };
            slideOut.Completed += (s, args) =>
            {
                ConfigPanel.Visibility = Visibility.Collapsed;
                _isAnimating = false;
            };

            var transform = ConfigPanel.RenderTransform as TranslateTransform;
            if (transform != null)
            {
                transform.BeginAnimation(TranslateTransform.YProperty, slideOut);
            }
            else
            {
                ConfigPanel.Visibility = Visibility.Collapsed;
                _isAnimating = false;
            }
            ConfigPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        // =====================================================================
        // 页签切换
        // =====================================================================
        private void Button_SwitchWindow(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedIndex = 0;
        }

        private void Button_SwitchAlarm(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedIndex = 1;
        }

        private void Button_SwitchDiagnosis(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedIndex = 2;
        }

        private void Button_SwitchSlave(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedIndex = 3;
        }
    }
}
