using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using AirCompressMonitor.Comm.Diagnostics;
using AirCompressMonitor.Comm.Models;
using AirCompressMonitor.Comm.Modbus;
using AirCompressMonitor.Storage;

namespace AirCompressMonitor.Wpf.ViewModel
{
    /// <summary>链路类型下拉项。</summary>
    public class LinkKindItem
    {
        public LinkKind Kind { get; set; }
        public string Text { get; set; }
        public override string ToString() { return Text; }
    }

    /// <summary>存储模式下拉项。</summary>
    public class StoreModeItem
    {
        public StoreMode Mode { get; set; }
        public string Text { get; set; }
        public override string ToString() { return Text; }
    }

    /// <summary>
    /// 从站状态行。多从站轮询时，界面上要能一眼看出「哪台在采、哪台掉了」。
    /// </summary>
    public class SlaveStatusRow : NotifyBase
    {
        private bool _online;
        private string _lastSeenText = "—";
        private int _failures;

        public byte SlaveId { get; set; }
        public string Name { get; set; }

        public bool Online
        {
            get { return _online; }
            set { SetField(ref _online, value); }
        }

        public string LastSeenText
        {
            get { return _lastSeenText; }
            set { SetField(ref _lastSeenText, value); }
        }

        public int Failures
        {
            get { return _failures; }
            set { SetField(ref _failures, value); }
        }

        public override string ToString() { return string.Format("{0}#{1}", Name, SlaveId); }
    }

    /// <summary>
    /// 主视图模型。UI 与业务的分界线就在这里：
    /// 界面只认属性与命令，采集、解析、诊断、落盘全在 Comm / Storage 两层，
    /// 换个界面（WinForm 工具集）能复用同一套底层。
    /// </summary>
    public class MainViewModel : NotifyBase, IDisposable
    {
        // =====================================================================
        // 依赖
        // =====================================================================
        private readonly IExceptionAnalyzer _analyzer = new RuleBasedExceptionAnalyzer();

        private IModbusLink _link;
        private ModbusMonitorClient _client;
        private DataStoreHub _store;
        private DataStoreOptions _storeOptions;

        private bool _disposed;

        /// <summary>界面提示（顶部红字）。用事件而不是直接抓 MainWindow，避免 VM 反向依赖视图。</summary>
        public event Action<string> Tip;

        // =====================================================================
        // 链路配置（界面上可改）
        // =====================================================================
        public ObservableCollection<LinkKindItem> LinkKinds { get; private set; }
        public ObservableCollection<StoreModeItem> StoreModes { get; private set; }
        public ObservableCollection<string> BaudRates { get; private set; }

        private LinkKindItem _selectedLinkKind;
        public LinkKindItem SelectedLinkKind
        {
            get { return _selectedLinkKind; }
            set
            {
                if (SetField(ref _selectedLinkKind, value))
                {
                    OnPropertyChanged("IsSerialSelected");
                    OnPropertyChanged("IsTcpSelected");
                }
            }
        }

        /// <summary>供界面按链路类型显示/隐藏串口那一组控件。</summary>
        public bool IsSerialSelected
        {
            get { return _selectedLinkKind == null || _selectedLinkKind.Kind == LinkKind.SerialRtu; }
        }

        public bool IsTcpSelected
        {
            get { return _selectedLinkKind != null && _selectedLinkKind.Kind == LinkKind.Tcp; }
        }

        private string _portName = "";
        public string PortName
        {
            get { return _portName; }
            set { SetField(ref _portName, value); }
        }

        private string _baudRate = "9600";
        public string BaudRate
        {
            get { return _baudRate; }
            set { SetField(ref _baudRate, value); }
        }

        private string _tcpHost = "192.168.1.10";
        public string TcpHost
        {
            get { return _tcpHost; }
            set { SetField(ref _tcpHost, value); }
        }

        private string _tcpPort = "502";
        public string TcpPort
        {
            get { return _tcpPort; }
            set { SetField(ref _tcpPort, value); }
        }

        private string _slaveId = "1";
        public string SlaveIdText
        {
            get { return _slaveId; }
            set { SetField(ref _slaveId, value); }
        }

        private string _slaveName = "1#空压机";
        public string SlaveName
        {
            get { return _slaveName; }
            set { SetField(ref _slaveName, value); }
        }

        private StoreModeItem _selectedStoreMode;
        public StoreModeItem SelectedStoreMode
        {
            get { return _selectedStoreMode; }
            set { SetField(ref _selectedStoreMode, value); }
        }

        // =====================================================================
        // 连接状态
        // =====================================================================
        private bool _isConnected;
        public bool IsConnected
        {
            get { return _isConnected; }
            private set
            {
                if (SetField(ref _isConnected, value))
                {
                    OnPropertyChanged("ConnectButtonText");
                    OnPropertyChanged("ConnectionText");
                }
            }
        }

        public string ConnectButtonText { get { return _isConnected ? "断开设备" : "连接设备"; } }

        private string _connectionText = "未连接";
        public string ConnectionText
        {
            get { return _connectionText; }
            private set { SetField(ref _connectionText, value); }
        }

        // =====================================================================
        // 实时数据（XAML 直接绑定这些名字）
        // =====================================================================
        private double _pressureValue;
        public double PressureValue
        {
            get { return _pressureValue; }
            private set { SetField(ref _pressureValue, value); }
        }

        private double _flow;
        public double Flow
        {
            get { return _flow; }
            private set { SetField(ref _flow, value); }
        }

        private int _time;
        public int Time
        {
            get { return _time; }
            private set { SetField(ref _time, value); }
        }

        private ushort _reg4Value;
        public ushort Reg4Value
        {
            get { return _reg4Value; }
            private set { SetField(ref _reg4Value, value); }
        }

        private bool _bit0, _bit1, _bit2, _bit3, _bit4, _bit5, _bit6, _bit7, _bit8;
        public bool Bit0 { get { return _bit0; } private set { SetField(ref _bit0, value); } }
        public bool Bit1 { get { return _bit1; } private set { SetField(ref _bit1, value); } }
        public bool Bit2 { get { return _bit2; } private set { SetField(ref _bit2, value); } }
        public bool Bit3 { get { return _bit3; } private set { SetField(ref _bit3, value); } }
        public bool Bit4 { get { return _bit4; } private set { SetField(ref _bit4, value); } }
        public bool Bit5 { get { return _bit5; } private set { SetField(ref _bit5, value); } }
        public bool Bit6 { get { return _bit6; } private set { SetField(ref _bit6, value); } }
        public bool Bit7 { get { return _bit7; } private set { SetField(ref _bit7, value); } }
        public bool Bit8 { get { return _bit8; } private set { SetField(ref _bit8, value); } }

        private bool _ctrlB0, _ctrlB1, _ctrlB2, _ctrlB3;
        public bool CtrlB0 { get { return _ctrlB0; } private set { SetField(ref _ctrlB0, value); } }
        public bool CtrlB1 { get { return _ctrlB1; } private set { SetField(ref _ctrlB1, value); } }
        public bool CtrlB2 { get { return _ctrlB2; } private set { SetField(ref _ctrlB2, value); } }
        public bool CtrlB3 { get { return _ctrlB3; } private set { SetField(ref _ctrlB3, value); } }

        /// <summary>本帧实际采用的字节序，显示出来让「自适应」不是一句空话。</summary>
        private string _endianText = "—";
        public string EndianText
        {
            get { return _endianText; }
            private set { SetField(ref _endianText, value); }
        }

        // =====================================================================
        // 通信统计（「界面稳定」要能被量化看见）
        // =====================================================================
        private string _successRateText = "—";
        public string SuccessRateText
        {
            get { return _successRateText; }
            private set { SetField(ref _successRateText, value); }
        }

        private string _latencyText = "—";
        public string LatencyText
        {
            get { return _latencyText; }
            private set { SetField(ref _latencyText, value); }
        }

        private string _reconnectText = "0";
        public string ReconnectText
        {
            get { return _reconnectText; }
            private set { SetField(ref _reconnectText, value); }
        }

        private string _requestText = "0";
        public string RequestText
        {
            get { return _requestText; }
            private set { SetField(ref _requestText, value); }
        }

        private string _storeText = "未启用";
        public string StoreText
        {
            get { return _storeText; }
            private set { SetField(ref _storeText, value); }
        }

        // =====================================================================
        // 集合
        // =====================================================================
        public ObservableCollection<AlarmInfo> AlarmList { get; private set; }
        public ObservableCollection<FaultDiagnosis> DiagnosisList { get; private set; }
        public ObservableCollection<SlaveStatusRow> SlaveStatuses { get; private set; }

        /// <summary>报警列表上限。现场连续跑几天，无上限会一直吃内存。</summary>
        public int MaxAlarmCount { get; set; }

        /// <summary>诊断列表上限。</summary>
        public int MaxDiagnosisCount { get; set; }

        private AlarmInfo _selectedAlarm;
        public AlarmInfo SelectedAlarm
        {
            get { return _selectedAlarm; }
            set { SetField(ref _selectedAlarm, value); }
        }

        private FaultDiagnosis _selectedDiagnosis;
        public FaultDiagnosis SelectedDiagnosis
        {
            get { return _selectedDiagnosis; }
            set { SetField(ref _selectedDiagnosis, value); }
        }

        private bool _isConfigExpanded;
        public bool IsConfigExpanded
        {
            get { return _isConfigExpanded; }
            set { SetField(ref _isConfigExpanded, value); }
        }

        // =====================================================================
        // 命令
        // =====================================================================
        public ICommand ToggleStartStopCommand { get; private set; }
        public ICommand ToggleLoadUnloadCommand { get; private set; }
        public ICommand DeleteAlarmCommand { get; private set; }
        public ICommand ClearAllAlarmCommand { get; private set; }
        public ICommand ExportAlarmCommand { get; private set; }
        public ICommand DeleteSingleAlarmCommand { get; private set; }
        public ICommand ClearDiagnosisCommand { get; private set; }
        public ICommand ExportDiagnosisCommand { get; private set; }
        public ICommand ToggleConfigCommand { get; private set; }

        public MainViewModel()
        {
            AlarmList = new ObservableCollection<AlarmInfo>();
            DiagnosisList = new ObservableCollection<FaultDiagnosis>();
            SlaveStatuses = new ObservableCollection<SlaveStatusRow>();
            MaxAlarmCount = 500;
            MaxDiagnosisCount = 200;

            LinkKinds = new ObservableCollection<LinkKindItem>
            {
                new LinkKindItem { Kind = LinkKind.SerialRtu, Text = "串口 RTU（RS485 / RS232）" },
                new LinkKindItem { Kind = LinkKind.Tcp,       Text = "以太网 TCP（Modbus TCP）" }
            };
            _selectedLinkKind = LinkKinds[0];

            StoreModes = new ObservableCollection<StoreModeItem>
            {
                new StoreModeItem { Mode = StoreMode.Both,   Text = "SQLite + CSV 双写" },
                new StoreModeItem { Mode = StoreMode.Sqlite, Text = "仅 SQLite" },
                new StoreModeItem { Mode = StoreMode.Csv,    Text = "仅 CSV" }
            };
            _selectedStoreMode = StoreModes[0];

            BaudRates = new ObservableCollection<string> { "4800", "9600", "19200", "38400", "57600", "115200" };

            ToggleStartStopCommand = new RelayCommand(ExecuteToggleStartStop);
            ToggleLoadUnloadCommand = new RelayCommand(ExecuteToggleLoadUnload);
            DeleteAlarmCommand = new RelayCommand(DeleteSelectedAlarm);
            ClearAllAlarmCommand = new RelayCommand(ClearAllAlarms);
            DeleteSingleAlarmCommand = new RelayCommand<AlarmInfo>(DeleteSingleAlarm);
            ClearDiagnosisCommand = new RelayCommand(ClearDiagnosis);
            // 导出方法返回落盘路径（供测试断言），命令契约是 Action，包一层即可
            ExportAlarmCommand = new RelayCommand(() => ExportAlarms());
            ExportDiagnosisCommand = new RelayCommand(() => ExportDiagnosis());
            ToggleConfigCommand = new RelayCommand(() => IsConfigExpanded = !IsConfigExpanded);
        }

        // =====================================================================
        // 跨线程调度
        // =====================================================================
        /// <summary>
        /// 把动作切回 UI 线程。用 BeginInvoke 而不是 Invoke：
        /// Invoke 会让采集线程等界面画完，界面一卡采集就积压 ——
        /// 「高并发采集下界面稳定」靠的正是采集线程永远不等 UI。
        /// </summary>
        private static void Ui(Action action)
        {
            if (action == null) return;
            var app = Application.Current;
            if (app == null || app.Dispatcher.CheckAccess())
            {
                action();
                return;
            }
            app.Dispatcher.BeginInvoke(action);
        }

        private void ShowTip(string message)
        {
            var handler = Tip;
            if (handler != null) Ui(() => handler(message));
        }

        // =====================================================================
        // 连接 / 断开
        // =====================================================================
        /// <summary>
        /// 按当前界面配置建立链路并开始采集。
        /// 返回 false 只代表「首次打开没成功」；此时重连流程已经在后台跑了，
        /// 界面显示「正在重连」而不是把用户挡在门外。
        /// </summary>
        public bool Connect()
        {
            Disconnect();

            try
            {
                _link = BuildLink();
            }
            catch (Exception ex)
            {
                Report(ex, "链路参数");
                ShowTip("配置有误：" + ex.Message);
                return false;
            }

            try
            {
                _store = BuildStore();
            }
            catch (Exception ex)
            {
                Report(ex, "存储初始化");
                ShowTip("存储初始化失败（采集继续，数据不落盘）：" + ex.Message);
                _store = null;
            }

            var client = new ModbusMonitorClient(_link);
            client.Slaves.Add(new SlaveConfig
            {
                SlaveId = ParseSlaveId(),
                Name = string.IsNullOrWhiteSpace(SlaveName) ? "空压机" : SlaveName.Trim(),
                StartAddress = 0,
                RegisterCount = 5,
                PollIntervalMs = 1000,
                Endian = EndianMode.Auto,      // 交给自适应判定
                PressureScale = 1.0,
                FlowScale = 0.1,
                Enabled = true
            });

            client.DataReceived += OnDataReceived;
            client.AlarmRaised += OnAlarmRaised;
            client.ConnectionChanged += OnConnectionChanged;
            client.Error += OnClientError;
            client.StatisticsUpdated += OnStatisticsUpdated;

            _client = client;

            Ui(() =>
            {
                SlaveStatuses.Clear();
                foreach (var s in client.Slaves)
                {
                    SlaveStatuses.Add(new SlaveStatusRow { SlaveId = s.SlaveId, Name = s.Name });
                }
                if (_store != null) StoreText = _store.Describe();
            });

            client.Start();     // 内部吞掉首次失败并转入重连，不抛
            IsConnected = client.IsConnected;
            ConnectionText = IsConnected ? client.LinkName : "正在重连…";

            if (IsConnected) ShowTip("已连接 " + client.LinkName);
            else ShowTip("首次连接未成功，已在后台自动重连：" + client.LinkName);

            return IsConnected;
        }

        public void Disconnect()
        {
            var client = _client;
            _client = null;
            if (client != null)
            {
                client.DataReceived -= OnDataReceived;
                client.AlarmRaised -= OnAlarmRaised;
                client.ConnectionChanged -= OnConnectionChanged;
                client.Error -= OnClientError;
                client.StatisticsUpdated -= OnStatisticsUpdated;
                try { client.Stop(); } catch (Exception ex) { Report(ex, "停止采集"); }
                try { client.Dispose(); } catch { }
            }

            if (_link != null)
            {
                try { _link.Dispose(); } catch { }
                _link = null;
            }

            var store = _store;
            _store = null;
            if (store != null)
            {
                try { store.Dispose(); } catch (Exception ex) { Report(ex, "关闭存储"); }
            }

            IsConnected = false;
            ConnectionText = "未连接";
            StoreText = "未启用";
        }

        private IModbusLink BuildLink()
        {
            if (SelectedLinkKind != null && SelectedLinkKind.Kind == LinkKind.Tcp)
            {
                int port;
                if (!int.TryParse(TcpPort, out port) || port <= 0 || port > 65535)
                    throw new ArgumentOutOfRangeException("TcpPort", "端口需在 1-65535 之间");
                if (string.IsNullOrWhiteSpace(TcpHost))
                    throw new ArgumentException("IP / 主机名不能为空");

                return new TcpLink(TcpHost.Trim(), port);
            }

            if (string.IsNullOrWhiteSpace(PortName))
                throw new ArgumentException("串口名不能为空");

            int baud;
            if (!int.TryParse(BaudRate, out baud) || baud <= 0)
                throw new ArgumentOutOfRangeException("BaudRate", "波特率必须是正整数");

            return new SerialRtuLink(PortName.Trim(), baud);
        }

        private DataStoreHub BuildStore()
        {
            _storeOptions = new DataStoreOptions
            {
                RootDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data"),
                Mode = SelectedStoreMode == null ? StoreMode.Both : SelectedStoreMode.Mode,
                RetentionDays = 30,
                BatchSize = 100,
                FlushIntervalMs = 5000
            };
            _storeOptions.Normalize();

            var hub = new DataStoreHub(_storeOptions);
            hub.Error += OnStoreError;
            hub.Initialize();
            return hub;
        }

        private byte ParseSlaveId()
        {
            byte id;
            if (!byte.TryParse(SlaveIdText, out id) || id < 1 || id > 247)
                throw new ArgumentOutOfRangeException("SlaveId", "从站地址需在 1-247 之间");
            return id;
        }

        // =====================================================================
        // 采集回调（后台线程）
        // =====================================================================
        private void OnDataReceived(DeviceData data)
        {
            if (data == null) return;

            // 落盘在采集线程做，不进 UI 队列：存储是 IO 密集的，
            // 塞进 Dispatcher 会让界面被磁盘拖慢。
            var store = _store;
            if (store != null)
            {
                try { store.Append(data); }
                catch (Exception ex) { Report(ex, "写入存储"); }
            }

            Ui(() =>
            {
                PressureValue = data.PressureValue;
                Flow = data.Flow;
                Time = data.Time;
                Reg4Value = data.Reg4Value;

                Bit0 = data.Bit0; Bit1 = data.Bit1; Bit2 = data.Bit2;
                Bit3 = data.Bit3; Bit4 = data.Bit4; Bit5 = data.Bit5;
                Bit6 = data.Bit6; Bit7 = data.Bit7; Bit8 = data.Bit8;

                CtrlB0 = data.CtrlB0; CtrlB1 = data.CtrlB1;
                CtrlB2 = data.CtrlB2; CtrlB3 = data.CtrlB3;

                EndianText = data.Endian.ToString();

                MarkOnline(data.SlaveId, true);
            });
        }

        private void OnAlarmRaised(AlarmInfo alarm)
        {
            if (alarm == null) return;

            FaultDiagnosis diagnosis = null;
            try { diagnosis = _analyzer.AnalyzeAlarm(alarm); }
            catch (Exception ex) { Report(ex, "报警诊断"); }

            Ui(() =>
            {
                AddCapped(AlarmList, alarm, MaxAlarmCount);
                if (diagnosis != null)
                {
                    diagnosis.SlaveId = alarm.SlaveId;
                    diagnosis.SlaveName = alarm.SlaveName;
                    AddCapped(DiagnosisList, diagnosis, MaxDiagnosisCount);
                }
                if (alarm.Level == AlarmLevel.Fault)
                    ShowTip(alarm.SlaveName + " " + alarm.Message);
            });
        }

        private void OnConnectionChanged(bool connected, string message)
        {
            Ui(() =>
            {
                IsConnected = connected;
                ConnectionText = connected ? message : (message + "，正在重连…");
                if (!connected)
                {
                    foreach (var row in SlaveStatuses) row.Online = false;
                }
            });
        }

        private void OnClientError(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            ShowTip(message);
        }

        private void OnStatisticsUpdated(CommStatistics stats)
        {
            if (stats == null) return;
            Ui(() =>
            {
                SuccessRateText = (stats.SuccessRate * 100.0).ToString("F1", CultureInfo.InvariantCulture) + "%";
                LatencyText = stats.LastLatencyMs.ToString("F0", CultureInfo.InvariantCulture) + " ms";
                ReconnectText = stats.ReconnectCount.ToString(CultureInfo.InvariantCulture);
                RequestText = stats.TotalRequests.ToString(CultureInfo.InvariantCulture);
            });
        }

        private void OnStoreError(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            ShowTip("存储：" + message);
        }

        /// <summary>把异常翻译成人话，同时留一条可追溯的原始记录。</summary>
        private void Report(Exception ex, string context)
        {
            if (ex == null) return;
            FaultDiagnosis diagnosis = null;
            try { diagnosis = _analyzer.Analyze(ex, context); }
            catch { /* 诊断本身出错不能再抛，否则把原始故障盖掉 */ }

            if (diagnosis != null)
            {
                Ui(() => AddCapped(DiagnosisList, diagnosis, MaxDiagnosisCount));
            }
            System.Diagnostics.Debug.WriteLine(string.Format("[{0:HH:mm:ss}] {1}: {2}", DateTime.Now, context, ex.Message));
        }

        private void MarkOnline(byte slaveId, bool online)
        {
            foreach (var row in SlaveStatuses)
            {
                if (row.SlaveId != slaveId) continue;
                row.Online = online;
                row.Failures = 0;
                row.LastSeenText = DateTime.Now.ToString("HH:mm:ss");
                break;
            }
        }

        private static void AddCapped<T>(ObservableCollection<T> list, T item, int cap)
        {
            list.Insert(0, item);       // 最新的在最上面，值班的人不用往下翻
            while (cap > 0 && list.Count > cap)
            {
                list.RemoveAt(list.Count - 1);
            }
        }

        // =====================================================================
        // 远程控制
        // =====================================================================
        private void ExecuteToggleStartStop()
        {
            var client = _client;
            if (client == null || !client.IsConnected)
            {
                ShowTip("请先连接设备");
                return;
            }

            try
            {
                // CtrlB0：当前在跑就发停止，反之发启动。单比特写，其余控制位不受影响。
                bool turnOn = !CtrlB0;
                if (client.WriteControlBit(ParseSlaveId(), 4, Reg4Value, 0, turnOn))
                {
                    ShowTip(turnOn ? "已下发远程启动" : "已下发远程停止");
                }
                else
                {
                    ShowTip("远程启停下发失败，请检查链路");
                }
            }
            catch (Exception ex)
            {
                Report(ex, "远程启停");
                ShowTip("操作失败：" + ex.Message);
            }
        }

        private void ExecuteToggleLoadUnload()
        {
            var client = _client;
            if (client == null || !client.IsConnected)
            {
                ShowTip("请先连接设备");
                return;
            }

            try
            {
                // 加载 / 卸载是 bit2 与 bit3 的一对互斥位，必须一次写进去。
                // 分两次写会出现「两位同时为 1」的中间态，设备侧可能按故障处理。
                ushort value = Reg4Value;
                if (CtrlB2)
                {
                    value = (ushort)(value & ~(1 << 2));
                    value = (ushort)(value | (1 << 3));
                }
                else
                {
                    value = (ushort)(value & ~(1 << 3));
                    value = (ushort)(value | (1 << 2));
                }

                if (client.WriteSingleRegister(ParseSlaveId(), 4, value))
                {
                    ShowTip(CtrlB2 ? "已切换为卸载" : "已切换为加载");
                }
                else
                {
                    ShowTip("加载/卸载下发失败，请检查链路");
                }
            }
            catch (Exception ex)
            {
                Report(ex, "远程加载/卸载");
                ShowTip("操作失败：" + ex.Message);
            }
        }

        // =====================================================================
        // 报警管理
        // =====================================================================
        private void DeleteSingleAlarm(AlarmInfo alarm)
        {
            if (alarm == null) return;
            AlarmList.Remove(alarm);
            ShowTip("已删除选中的报警记录");
        }

        private void DeleteSelectedAlarm()
        {
            if (SelectedAlarm == null)
            {
                ShowTip("请选择要删除的报警记录");
                return;
            }
            AlarmList.Remove(SelectedAlarm);
            SelectedAlarm = null;
            ShowTip("已删除选中的报警记录");
        }

        private void ClearAllAlarms()
        {
            AlarmList.Clear();
            ShowTip("已清除所有报警记录");
        }

        /// <summary>导出报警记录。返回写出的路径，取消时返回 null。</summary>
        public string ExportAlarms()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = string.Format("AlarmLog_{0:yyyyMMdd_HHmmss}.csv", DateTime.Now)
            };
            if (dialog.ShowDialog() != true) return null;

            try
            {
                // UTF8 BOM：不带 BOM 的话 Excel 打开中文是乱码。
                using (var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine("时间,从站号,从站名,状态,级别,描述");
                    foreach (var alarm in AlarmList)
                    {
                        writer.WriteLine(string.Join(",",
                            Escape(alarm.Time.ToString("yyyy-MM-dd HH:mm:ss")),
                            alarm.SlaveId.ToString(CultureInfo.InvariantCulture),
                            Escape(alarm.SlaveName),
                            Escape(alarm.State),
                            Escape(alarm.Level.ToString()),
                            Escape(alarm.Message)));
                    }
                }
                ShowTip("报警记录已导出：" + dialog.FileName);
                return dialog.FileName;
            }
            catch (Exception ex)
            {
                Report(ex, "导出报警");
                ShowTip("导出失败：" + ex.Message);
                return null;
            }
        }

        private void ClearDiagnosis()
        {
            DiagnosisList.Clear();
            ShowTip("已清除诊断记录");
        }

        public string ExportDiagnosis()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = string.Format("Diagnosis_{0:yyyyMMdd_HHmmss}.csv", DateTime.Now)
            };
            if (dialog.ShowDialog() != true) return null;

            try
            {
                using (var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine("时间,从站号,分类,现象,原因,处置建议,原始错误");
                    foreach (var d in DiagnosisList)
                    {
                        writer.WriteLine(string.Join(",",
                            Escape(d.Time.ToString("yyyy-MM-dd HH:mm:ss")),
                            d.SlaveId.ToString(CultureInfo.InvariantCulture),
                            Escape(d.Category.ToString()),
                            Escape(d.Symptom),
                            Escape(d.Cause),
                            Escape(d.Suggestion),
                            Escape(d.RawError)));
                    }
                }
                ShowTip("诊断记录已导出：" + dialog.FileName);
                return dialog.FileName;
            }
            catch (Exception ex)
            {
                Report(ex, "导出诊断");
                ShowTip("导出失败：" + ex.Message);
                return null;
            }
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.IndexOf(',') < 0 && text.IndexOf('"') < 0 && text.IndexOf('\n') < 0) return text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>刷新可选串口列表，供界面在打开配置面板时调用。</summary>
        public static string[] AvailablePorts()
        {
            try { return System.IO.Ports.SerialPort.GetPortNames(); }
            catch { return new string[0]; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
            GC.SuppressFinalize(this);
        }
    }
}
