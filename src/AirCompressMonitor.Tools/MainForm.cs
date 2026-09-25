using System;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Modbus.Device;
using AirCompressMonitor.Comm.Diagnostics;
using AirCompressMonitor.Comm.Models;
using AirCompressMonitor.Comm.Modbus;
using AirCompressMonitor.Comm.Profiles;
using AirCompressMonitor.Storage;

namespace AirCompressMonitor.Tools
{
    /// <summary>
    /// 工业设备监控工具集主窗体。
    ///
    /// 与正式监控软件的分工：正式软件负责「长期在线采集 + 落库 + 界面」，
    /// 工具集负责「到现场那一刻的排查」—— 这条线上挂了谁、地址对不对、
    /// 某个寄存器现在是多少、这个品牌该按什么字节序解。
    /// 所以四个页签就是四件现场最常干的事，没有多余的壳。
    ///
    /// 一个刻意的约束：串口是独占资源，三个页签共用同一个口。
    /// 所以链路参数只在「从站扫描」页配一次，另外两页只显示当前用的是哪条链路，
    /// 并且三条路径之间会互相让出串口 —— 否则后开的那个必然报「串口被占用」，
    /// 现场很容易误判成线坏了。
    /// </summary>
    public partial class MainForm : Form
    {
        private static readonly int[] BaudRateItems = { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };

        /// <summary>读功能码选项。诊断只用到这两种。</summary>
        private static readonly string[] ReadFuncs = { "03 读保持寄存器", "04 读输入寄存器" };

        private const int MaxCollectRows = 200;
        private const int MaxDiagRows = 300;
        private const int MaxLogLines = 500;
        private const int CollectPollIntervalMs = 1000;

        private readonly IExceptionAnalyzer _analyzer = new RuleBasedExceptionAnalyzer();
        private readonly System.Windows.Forms.Timer _pollTimer = new System.Windows.Forms.Timer();

        // ---- 扫描页 ----
        private CancellationTokenSource _scanCts;

        // ---- 监视页：会话保持打开，避免每次读都重开串口 ----
        private ModbusSession _session;
        private string _sessionKey;
        private bool _reading;

        // ---- 采集页 ----
        private ModbusMonitorClient _client;
        private DataStoreHub _store;
        private long _frames;

        private PlcBrandProfile _brand = PlcBrandCatalog.All[0];

        public MainForm()
        {
            InitializeComponent();

            BuildColumns();
            InitPickers();
            WireEvents();

            RefreshPorts();
            ApplyBrand(_brand);
            SetScanRunning(false);
            SetCollectRunning(false);
            UpdateLinkLabels();
        }

        /// <summary>
        /// 事件接线统一放在这里，而不是散在 InitializeComponent 里。
        ///
        /// 本机没有 Visual Studio，Designer 文件是手写的，设计器那一栏本来也看不见事件；
        /// 接线集中成一处，改界面时不用在两个文件之间来回找。
        /// </summary>
        private void WireEvents()
        {
            btnScanRefreshPorts.Click += btnScanRefreshPorts_Click;
            btnScanStart.Click += btnScanStart_Click;
            btnScanStop.Click += btnScanStop_Click;

            cboBrand.SelectedIndexChanged += cboBrand_SelectedIndexChanged;
            btnApplyBrand.Click += btnApplyBrand_Click;
            btnReadOnce.Click += btnReadOnce_Click;
            chkAutoPoll.CheckedChanged += chkAutoPoll_CheckedChanged;

            btnCollectStart.Click += btnCollectStart_Click;
            btnCollectStop.Click += btnCollectStop_Click;

            btnDiagClear.Click += btnDiagClear_Click;
            btnDiagExport.Click += btnDiagExport_Click;

            _pollTimer.Tick += (s, e) => ReadOnce();

            // 链路参数一变，两个只读标签要跟着走，否则会显示成另一条链路
            cboScanPort.TextChanged += (s, e) => UpdateLinkLabels();
            cboScanBaud.TextChanged += (s, e) => UpdateLinkLabels();
            numPollMs.ValueChanged += (s, e) => { if (_pollTimer.Enabled) _pollTimer.Interval = (int)numPollMs.Value; };

            FormClosing += MainForm_FormClosing;
        }

        // =====================================================================
        // 初始化
        // =====================================================================

        private static void AddCol(DataGridView grid, string name, string header, float weight)
        {
            var col = new DataGridViewTextBoxColumn();
            col.Name = name;
            col.HeaderText = header;
            col.FillWeight = weight;
            col.ReadOnly = true;
            col.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(col);
        }

        /// <summary>
        /// 表格列在代码里建。四个表格都是只读展示，用非绑定模式逐行填，
        /// 比维护一套绑定对象更直接 —— 这里的行是「一次读取的快照」，不是实体。
        /// </summary>
        private void BuildColumns()
        {
            AddCol(dgvScan, "SlaveId", "从站号", 55);
            AddCol(dgvScan, "State", "状态", 70);
            AddCol(dgvScan, "Elapsed", "耗时(ms)", 65);
            AddCol(dgvScan, "Note", "说明", 300);

            AddCol(dgvMon, "Address", "地址", 95);
            AddCol(dgvMon, "Hex", "原始 HEX", 75);
            AddCol(dgvMon, "Unsigned", "无符号", 65);
            AddCol(dgvMon, "Signed", "有符号", 65);
            AddCol(dgvMon, "Note", "说明", 320);

            AddCol(dgvCollect, "Time", "时间", 70);
            AddCol(dgvCollect, "Slave", "从站", 45);
            AddCol(dgvCollect, "Pressure", "压力", 70);
            AddCol(dgvCollect, "Flow", "流量", 70);
            AddCol(dgvCollect, "RunTime", "运行时间", 70);
            AddCol(dgvCollect, "Status", "状态字", 70);
            AddCol(dgvCollect, "Control", "控制字", 70);
            AddCol(dgvCollect, "Endian", "字节序", 100);

            AddCol(dgvDiag, "Time", "时间", 65);
            AddCol(dgvDiag, "Slave", "从站", 45);
            AddCol(dgvDiag, "Category", "分类", 60);
            AddCol(dgvDiag, "Symptom", "现象", 160);
            AddCol(dgvDiag, "Cause", "原因", 220);
            AddCol(dgvDiag, "Suggestion", "处置建议", 240);
            AddCol(dgvDiag, "Level", "级别", 45);
        }

        private void InitPickers()
        {
            var bauds = new object[BaudRateItems.Length];
            for (int i = 0; i < BaudRateItems.Length; i++) bauds[i] = BaudRateItems[i];
            cboScanBaud.Items.AddRange(bauds);
            cboScanBaud.Text = "9600";

            cboScanFunc.Items.AddRange(ReadFuncs);
            cboScanFunc.SelectedIndex = 0;

            cboMonFunc.Items.AddRange(ReadFuncs);
            cboMonFunc.SelectedIndex = 0;

            foreach (var p in PlcBrandCatalog.All) cboBrand.Items.Add(p);
            cboBrand.SelectedIndex = 0;

            cboStoreMode.Items.Add("仅 SQLite");
            cboStoreMode.Items.Add("仅 CSV");
            cboStoreMode.Items.Add("SQLite + CSV（双模式）");
            cboStoreMode.SelectedIndex = 2;
        }

        private void RefreshPorts()
        {
            string keep = cboScanPort.Text;
            cboScanPort.Items.Clear();

            var ports = AvailablePorts();
            foreach (var p in ports) cboScanPort.Items.Add(p);

            if (!string.IsNullOrEmpty(keep) && cboScanPort.Items.Contains(keep))
                cboScanPort.Text = keep;
            else if (ports.Length > 0)
                cboScanPort.SelectedIndex = 0;
            else
                cboScanPort.Text = "";

            UpdateLinkLabels();
        }

        /// <summary>取可用串口。驱动异常不该让整个界面起不来，所以吞掉异常返回空表。</summary>
        private static string[] AvailablePorts()
        {
            try { return SerialPort.GetPortNames(); }
            catch { return new string[0]; }
        }

        // =====================================================================
        // 链路参数
        // =====================================================================

        /// <summary>
        /// 按界面上的串口参数建链路。
        ///
        /// 工具集按定位只做 Modbus RTU（串口）。要扩到 Modbus TCP，
        /// 把这里换成 new TcpLink(host, port) 即可 —— 会话层与扫描层只认
        /// IModbusLink，上层一行都不用改。
        /// </summary>
        private IModbusLink BuildLink(out string error)
        {
            error = null;

            string port = (cboScanPort.Text ?? "").Trim();
            if (port.Length == 0) { error = "请先选择串口号"; return null; }

            int baud = SelectedBaud();
            if (baud <= 0) { error = "波特率无效"; return null; }

            return new SerialRtuLink(port, baud);
        }

        private int SelectedBaud()
        {
            int baud;
            if (int.TryParse(Convert.ToString(cboScanBaud.Text).Trim(), out baud) && baud > 0) return baud;
            return 0;
        }

        private static bool SelectedFuncIsInput(ComboBox cbo)
        {
            return cbo.SelectedIndex == 1;
        }

        private StoreMode SelectedStoreMode()
        {
            switch (cboStoreMode.SelectedIndex)
            {
                case 0: return StoreMode.Sqlite;
                case 1: return StoreMode.Csv;
                default: return StoreMode.Both;
            }
        }

        private void UpdateLinkLabels()
        {
            string text = string.Format("链路：{0}", DescribeLink());
            lblMonLink.Text = text;
            lblCollectLink.Text = text;
        }

        private string DescribeLink()
        {
            string port = (cboScanPort.Text ?? "").Trim();
            if (port.Length == 0) return "未选择串口（在「从站扫描」页选）";
            return string.Format("{0}@{1},8,N,1（三页共用）", port, SelectedBaud());
        }

        /// <summary>把结果送回 UI 线程。窗体已在关闭时不抛异常，静默丢弃。</summary>
        private void RunOnUi(Action action)
        {
            if (action == null) return;
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            // ObjectDisposedException 继承自 InvalidOperationException，必须先捕获
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { /* 句柄已销毁 */ }
        }

        // =====================================================================
        // 页签一：从站扫描
        // =====================================================================

        private void btnScanRefreshPorts_Click(object sender, EventArgs e)
        {
            RefreshPorts();
        }

        private void btnScanStart_Click(object sender, EventArgs e)
        {
            if (_scanCts != null) return;

            if (_client != null)
            {
                lblScanStatus.Text = "采集正在运行，请先在「采集与落盘」页停止采集";
                return;
            }

            string error;
            var link = BuildLink(out error);
            if (link == null) { lblScanStatus.Text = error; return; }

            // 串口只能被一个会话持有，先把监视页的会话让出去
            ReleaseMonitorSession();

            byte from = (byte)numScanFrom.Value;
            byte to = (byte)numScanTo.Value;
            bool input = SelectedFuncIsInput(cboScanFunc);

            dgvScan.Rows.Clear();
            _scanCts = new CancellationTokenSource();
            SetScanRunning(true);
            lblScanStatus.Text = "扫描中…";

            var session = new ModbusSession(link);
            var scanner = new SlaveScanner();
            var token = _scanCts.Token;

            // 探测只读一个寄存器：扫描的目的是「这个地址上有没有东西」，
            // 多读几个既慢，又会在寄存器数量不足的设备上凭空失败
            Func<IModbusMaster, byte, ushort[]> probe = input
                ? (Func<IModbusMaster, byte, ushort[]>)((m, id) => m.ReadInputRegisters(id, 0, 1))
                : (Func<IModbusMaster, byte, ushort[]>)((m, id) => m.ReadHoldingRegisters(id, 0, 1));

            Task.Run(() => scanner.Scan(session, from, to, probe, AppendScanRow, token))
                .ContinueWith(t =>
                {
                    var results = t.IsFaulted ? null : t.Result;
                    string failure = t.IsFaulted ? t.Exception.GetBaseException().Message : null;

                    RunOnUi(() =>
                    {
                        session.Dispose();

                        var cts = _scanCts;
                        _scanCts = null;
                        if (cts != null) { try { cts.Dispose(); } catch { } }

                        SetScanRunning(false);

                        if (failure != null)
                        {
                            lblScanStatus.Text = "扫描失败：" + failure;
                            return;
                        }

                        int alive = 0, refused = 0;
                        foreach (var r in results)
                        {
                            if (!r.Alive) continue;
                            alive++;
                            if (r.RespondedWithException) refused++;
                        }

                        lblScanStatus.Text = string.Format("扫描完成：试了 {0} 个地址，{1} 个在线（其中 {2} 个拒绝该功能码）",
                            results.Count, alive, refused);
                    });
                });
        }

        private void btnScanStop_Click(object sender, EventArgs e)
        {
            var cts = _scanCts;
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            lblScanStatus.Text = "正在停止…";
        }

        private void AppendScanRow(ScanResult r)
        {
            RunOnUi(() =>
            {
                int i = dgvScan.Rows.Add(
                    r.SlaveId,
                    r.Alive ? (r.RespondedWithException ? "在线(拒绝)" : "在线") : "无应答",
                    r.ElapsedMs > 0 ? r.ElapsedMs.ToString("F0") : "-",
                    r.Note);

                // 上色：在线绿、应答异常橙、无应答灰 —— 一眼扫过去就知道哪几行要管
                dgvScan.Rows[i].DefaultCellStyle.ForeColor = !r.Alive
                    ? Color.Gray
                    : (r.RespondedWithException ? Color.DarkOrange : Color.ForestGreen);
            });
        }

        private void SetScanRunning(bool running)
        {
            btnScanStart.Enabled = !running;
            btnScanStop.Enabled = running;
            numScanFrom.Enabled = !running;
            numScanTo.Enabled = !running;
            cboScanFunc.Enabled = !running;
        }

        // =====================================================================
        // 页签二：寄存器监视
        // =====================================================================

        private void cboBrand_SelectedIndexChanged(object sender, EventArgs e)
        {
            var p = cboBrand.SelectedItem as PlcBrandProfile;
            if (p != null) _brand = p;
        }

        private void btnApplyBrand_Click(object sender, EventArgs e)
        {
            ApplyBrand(cboBrand.SelectedItem as PlcBrandProfile);
        }

        /// <summary>
        /// 应用品牌模板：地址与数量填进界面，字节序与换算系数留在 <see cref="_brand"/> 里，
        /// 由采集页生成从站配置时取用。模板的作用是给一个正确的起点，不是替人做决定，
        /// 所以填完之后每一项都还能手改。
        /// </summary>
        private void ApplyBrand(PlcBrandProfile p)
        {
            if (p == null) return;
            _brand = p;

            numMonStart.Value = Clamp(p.StartAddress, numMonStart.Minimum, numMonStart.Maximum);
            numMonCount.Value = Clamp(p.RegisterCount, numMonCount.Minimum, numMonCount.Maximum);

            lblBrandNote.Text = string.Format(
                "{0}｜起始地址 {1}（0x{1:X4}）｜{2} 个寄存器｜压力 ×{3}｜流量 ×{4}｜字节序 {5}\r\n{6}",
                p, p.StartAddress, p.RegisterCount, p.PressureScale, p.FlowScale, p.Endian, p.Note);

            SetMonStatus(string.Format("已应用模板：{0}", p));
        }

        private static decimal Clamp(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void btnReadOnce_Click(object sender, EventArgs e)
        {
            ReadOnce();
        }

        private void chkAutoPoll_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAutoPoll.Checked)
            {
                _pollTimer.Interval = (int)numPollMs.Value;
                _pollTimer.Start();
                SetMonStatus("自动轮询中…");
            }
            else
            {
                _pollTimer.Stop();
                SetMonStatus("已停止轮询");
            }
        }

        /// <summary>
        /// 读一次。读在后台线程上做 —— 设备不应答时要等满超时，
        /// 放在 UI 线程上会让整个窗口卡住，工具集最忌讳这个。
        /// </summary>
        private void ReadOnce()
        {
            // 上一次还没回来：宁可少读一帧，也不要把请求排队堆起来
            if (_reading) return;

            string error;
            var session = EnsureSession(out error);
            if (session == null)
            {
                SetMonStatus(error);
                StopAutoPoll("链路不可用");
                return;
            }

            byte slave = (byte)numMonSlave.Value;
            ushort start = (ushort)numMonStart.Value;
            ushort count = (ushort)numMonCount.Value;
            bool input = SelectedFuncIsInput(cboMonFunc);

            _reading = true;
            Task.Run(() => session.TryRead(slave, start, count, input))
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        string message = t.Exception.GetBaseException().Message;
                        RunOnUi(() => { _reading = false; SetMonStatus("读取异常：" + message); });
                        return;
                    }
                    var block = t.Result;
                    RunOnUi(() => ApplyRead(block, input));
                });
        }

        /// <summary>
        /// 取当前会话。链路参数没变就复用同一条已打开的链路，
        /// 变了才重建 —— 每次读都重开串口既慢又会和别的程序抢口。
        /// </summary>
        private ModbusSession EnsureSession(out string error)
        {
            error = null;

            string key = string.Format("{0}@{1}", (cboScanPort.Text ?? "").Trim(), SelectedBaud());
            if (_session != null && _sessionKey == key && _session.IsOpen) return _session;

            CloseSession();

            var link = BuildLink(out error);
            if (link == null) return null;

            var session = new ModbusSession(link);
            try
            {
                session.SetTimeouts(1000, 1000, 1);
                session.Open();
            }
            catch (Exception ex)
            {
                try { session.Dispose(); } catch { }
                error = string.Format("打开 {0} 失败：{1}", link.Name, SlaveScanner.Describe(ex));
                AddDiagnosis(_analyzer.Analyze(ex, string.Format("打开链路 {0}", link.Name)));
                return null;
            }

            _session = session;
            _sessionKey = key;
            return _session;
        }

        private void CloseSession()
        {
            var session = _session;
            _session = null;
            _sessionKey = null;
            if (session != null) { try { session.Dispose(); } catch { } }
        }

        /// <summary>停掉自动轮询并让出串口。扫描/采集开始前必须调它。</summary>
        private void ReleaseMonitorSession()
        {
            _pollTimer.Stop();
            chkAutoPoll.Checked = false;
            CloseSession();
        }

        private void StopAutoPoll(string reason)
        {
            if (!_pollTimer.Enabled) return;
            _pollTimer.Stop();
            chkAutoPoll.Checked = false;   // 会触发 CheckedChanged，里面只负责停表
            SetMonStatus(reason + "，已停止自动轮询");
        }

        private void ApplyRead(RegisterBlock block, bool input)
        {
            _reading = false;
            if (block == null) return;

            if (!block.Ok)
            {
                SetMonStatus(string.Format("读取失败：{0}（{1:F0} ms）", block.Error, block.ElapsedMs));

                // 有异常对象就交给诊断器归类，能得到「原因 + 处置建议」而不只是一句话
                if (block.Exception != null)
                {
                    AddDiagnosis(_analyzer.Analyze(block.Exception, string.Format("从站 {0} {1}",
                        numMonSlave.Value, input ? "读输入寄存器" : "读保持寄存器")));
                }
                return;
            }

            dgvMon.Rows.Clear();
            var values = block.Values;
            for (int i = 0; i < values.Length; i++)
            {
                ushort raw = values[i];
                ushort word = Endianness.ApplyWord(raw, _brand.Endian);
                dgvMon.Rows.Add(
                    string.Format("{0} / 0x{0:X4}", block.StartAddress + i),
                    string.Format("0x{0:X4}", raw),
                    raw.ToString(),
                    ((short)raw).ToString(),
                    BuildNote(i, raw, word));
            }

            SetMonStatus(string.Format("{0} 个寄存器，用时 {1:F0} ms", values.Length, block.ElapsedMs));
        }

        private string BuildNote(int index, ushort raw, ushort word)
        {
            var notes = _brand.RegisterNotes;
            string name = (notes != null && index < notes.Length) ? notes[index] : "未定义";

            // 按模板字节序还原后与原始值不同才提示，否则每一行都挂一句反而看不清
            if (word == raw) return name;
            return string.Format("{0}｜按 {1} 还原后 {2}", name, _brand.Endian, word);
        }

        private void SetMonStatus(string text)
        {
            lblMonStatus.Text = text;
        }

        // =====================================================================
        // 页签三：采集与落盘
        // =====================================================================

        private void btnCollectStart_Click(object sender, EventArgs e)
        {
            if (_client != null) return;

            if (_scanCts != null)
            {
                SetCollectStatus("扫描正在运行，请先停止扫描");
                return;
            }

            string error;
            var link = BuildLink(out error);
            if (link == null) { SetCollectStatus(error); return; }

            byte slaveId = (byte)numStoreSlave.Value;
            ushort start = (ushort)numStoreStart.Value;
            ushort count = (ushort)numStoreCount.Value;

            // 品牌模板负责「怎么解」（字节序与换算系数），本页负责「读哪儿」
            var slave = _brand.ToSlaveConfig(slaveId, string.Format("{0} 设备 {1}", _brand.Brand, slaveId));
            slave.StartAddress = start;
            slave.RegisterCount = count;
            slave.PollIntervalMs = CollectPollIntervalMs;

            var options = new DataStoreOptions
            {
                RootDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data"),
                Mode = SelectedStoreMode(),
                RetentionDays = 30,
                BatchSize = 100,
                FlushIntervalMs = 5000
            };

            DataStoreHub store = null;
            ModbusMonitorClient client = null;
            try
            {
                store = new DataStoreHub(options);
                store.Error += m => Log("存储：" + m);
                store.MaintenanceCompleted += r => Log("存储维护：" + r.Message);
                store.Initialize();

                client = new ModbusMonitorClient(link);
                client.Slaves.Add(slave);
                client.DataReceived += OnDataReceived;
                client.AlarmRaised += OnAlarmRaised;
                client.ConnectionChanged += OnConnectionChanged;
                client.Error += OnCommError;
                client.StatisticsUpdated += OnStatistics;

                _store = store;
                _client = client;

                // Start 不会因连不上而抛：连不上会自己进重连流程，界面保持可用
                client.Start();
            }
            catch (Exception ex)
            {
                if (client != null) { try { client.Dispose(); } catch { } }
                else { try { link.Dispose(); } catch { } }
                if (store != null) { try { store.Dispose(); } catch { } }
                _store = null;
                _client = null;

                SetCollectStatus("启动失败：" + ex.Message);
                AddDiagnosis(_analyzer.Analyze(ex, "启动采集"));
                Log("启动采集失败：" + ex.Message);
                return;
            }

            dgvCollect.Rows.Clear();
            Interlocked.Exchange(ref _frames, 0);
            SetCollectRunning(true);
            SetCollectStatus(string.Format("采集中｜{0}", store.Describe()));

            Log(string.Format("开始采集：{0}｜从站 {1}｜地址 {2} 起 {3} 个｜{4}",
                link.Name, slaveId, start, count, options.Mode));
            Log("数据目录：" + options.RootDirectory);
        }

        private void btnCollectStop_Click(object sender, EventArgs e)
        {
            var client = _client;
            var store = _store;
            _client = null;
            _store = null;

            if (client != null)
            {
                try { client.Stop(); } catch (Exception ex) { Log("停止采集异常：" + ex.Message); }
                try { client.Dispose(); } catch { }
            }

            if (store != null)
            {
                try { Log("落盘收尾：" + store.Describe()); } catch { }
                // Dispose 内部会先把缓冲 Flush 掉，不丢最后一批
                try { store.Dispose(); } catch { }
            }

            SetCollectRunning(false);
            SetCollectStatus(string.Format("已停止｜本次采集 {0} 帧", Interlocked.Read(ref _frames)));
        }

        private void OnDataReceived(DeviceData data)
        {
            // 落盘就放在采集线程上做：存储是 IO 密集的，
            // 挪到 UI 线程会让界面被磁盘拖慢 —— 这正是「界面稳定」要防的事
            var store = _store;
            if (store != null) { try { store.Append(data); } catch { } }

            Interlocked.Increment(ref _frames);

            RunOnUi(() => AppendCollectRow(data));
        }

        private void AppendCollectRow(DeviceData d)
        {
            var regs = d.RawRegisters;
            dgvCollect.Rows.Add(
                d.Timestamp.ToString("HH:mm:ss"),
                d.SlaveId,
                d.PressureValue.ToString("F2"),
                d.Flow.ToString("F2"),
                d.Time,
                string.Format("0x{0:X4}", regs != null && regs.Length > 3 ? regs[3] : (ushort)0),
                string.Format("0x{0:X4}", d.Reg4Value),
                d.Endian.ToString());

            // 只留最近若干行：工具集是看现场的，翻几小时前的帧没有意义，
            // 而无上限增长会同时吃掉内存和重绘时间
            while (dgvCollect.Rows.Count > MaxCollectRows)
                dgvCollect.Rows.RemoveAt(dgvCollect.Rows.Count - 1);
        }

        private void OnAlarmRaised(AlarmInfo alarm)
        {
            FaultDiagnosis diag = null;
            try { diag = _analyzer.AnalyzeAlarm(alarm); } catch { }
            if (diag == null) return;

            RunOnUi(() =>
            {
                AddDiagnosis(diag);
                Log("报警：" + alarm.Message);
            });
        }

        private void OnConnectionChanged(bool connected, string message)
        {
            RunOnUi(() =>
            {
                Log("链路：" + message);
                SetCollectStatus(message);
            });
        }

        /// <summary>
        /// 采集层报上来的错误只有文本、没有异常对象，按类型归类的诊断器在这里用不上。
        /// 与其硬套一个分类，不如照实记「现象 + 下一步做什么」——
        /// 而下一步恰好就是本工具集自己的两页：先扫描确认设备在不在，
        /// 再监视一次，区分「设备不应答」和「寄存器地址不对」。
        /// </summary>
        private void OnCommError(string message)
        {
            RunOnUi(() =>
            {
                Log("通信：" + message);
                AddDiagnosis(new FaultDiagnosis
                {
                    Time = DateTime.Now,
                    Category = FaultCategory.Communication,
                    Symptom = message,
                    Cause = "采集层报告的错误，原文见「现象」列",
                    Suggestion = "先用「从站扫描」页确认该地址是否有设备应答；再用「寄存器监视」页读一次，区分是设备不应答还是寄存器地址不对",
                    RawError = message,
                    Level = AlarmLevel.Warning
                });
            });
        }

        private void OnStatistics(CommStatistics st)
        {
            if (st == null) return;

            var store = _store;
            string storeText = store != null ? store.Describe() : "未启用";

            RunOnUi(() => SetCollectStatus(string.Format(
                "请求 {0}｜失败 {1}｜成功率 {2:P1}｜延迟 {3:F0} ms｜重连 {4} 次｜{5}",
                st.TotalRequests, st.FailedRequests, st.SuccessRate,
                st.LastLatencyMs, st.ReconnectCount, storeText)));
        }

        private void SetCollectRunning(bool running)
        {
            btnCollectStart.Enabled = !running;
            btnCollectStop.Enabled = running;
            numStoreSlave.Enabled = !running;
            numStoreStart.Enabled = !running;
            numStoreCount.Enabled = !running;
            cboStoreMode.Enabled = !running;
        }

        private void SetCollectStatus(string text)
        {
            lblCollectStatus.Text = text;
        }

        // =====================================================================
        // 页签四：通信诊断
        // =====================================================================

        /// <summary>诊断条目一律插到最前面：现场要看的是「刚刚出了什么事」。</summary>
        private void AddDiagnosis(FaultDiagnosis d)
        {
            if (d == null) return;

            dgvDiag.Rows.Insert(0,
                d.Time.ToString("HH:mm:ss"),
                d.SlaveId == 0 ? "-" : d.SlaveId.ToString(),
                d.Category,
                d.Symptom,
                d.Cause,
                d.Suggestion,
                d.Level);

            dgvDiag.Rows[0].DefaultCellStyle.ForeColor = LevelColor(d.Level);

            while (dgvDiag.Rows.Count > MaxDiagRows)
                dgvDiag.Rows.RemoveAt(dgvDiag.Rows.Count - 1);

            lblDiagCount.Text = string.Format("共 {0} 条", dgvDiag.Rows.Count);
        }

        private static Color LevelColor(AlarmLevel level)
        {
            switch (level)
            {
                case AlarmLevel.Fault: return Color.Firebrick;
                case AlarmLevel.Warning: return Color.DarkOrange;
                default: return Color.DimGray;
            }
        }

        private void btnDiagClear_Click(object sender, EventArgs e)
        {
            dgvDiag.Rows.Clear();
            lblDiagCount.Text = "共 0 条";
        }

        private void btnDiagExport_Click(object sender, EventArgs e)
        {
            if (dgvDiag.Rows.Count == 0)
            {
                MessageBox.Show(this, "没有可导出的诊断记录。", "导出",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string path;
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "CSV 文件|*.csv|文本文件|*.txt";
                dlg.FileName = string.Format("通信诊断_{0:yyyyMMdd_HHmmss}.csv", DateTime.Now);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }

            try
            {
                // 带 BOM：现场多半直接用 Excel 打开，不带 BOM 中文会显示成乱码
                using (var w = new StreamWriter(path, false, new UTF8Encoding(true)))
                {
                    w.WriteLine("时间,从站,分类,现象,原因,处置建议,级别");
                    foreach (DataGridViewRow row in dgvDiag.Rows)
                    {
                        var cells = new string[row.Cells.Count];
                        for (int i = 0; i < row.Cells.Count; i++)
                            cells[i] = Csv(row.Cells[i].Value == null ? "" : row.Cells[i].Value.ToString());
                        w.WriteLine(string.Join(",", cells));
                    }
                }

                Log("已导出诊断记录：" + path);
                MessageBox.Show(this, "已导出：\n" + path, "导出成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "导出",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        // =====================================================================
        // 日志与退出
        // =====================================================================

        private void Log(string message)
        {
            RunOnUi(() =>
            {
                txtCollectLog.AppendText(string.Format("[{0:HH:mm:ss}] {1}{2}",
                    DateTime.Now, message, Environment.NewLine));

                var lines = txtCollectLog.Lines;
                if (lines.Length <= MaxLogLines) return;

                // 长时间采集会把文本框吃成几百兆，只留最近一段
                var keep = new string[MaxLogLines / 2];
                Array.Copy(lines, lines.Length - keep.Length, keep, 0, keep.Length);
                txtCollectLog.Lines = keep;
                txtCollectLog.SelectionStart = txtCollectLog.TextLength;
                txtCollectLog.ScrollToCaret();
            });
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            var cts = _scanCts;
            if (cts != null) { try { cts.Cancel(); } catch { } }

            _pollTimer.Stop();
            CloseSession();

            var client = _client;
            _client = null;
            if (client != null) { try { client.Dispose(); } catch { } }

            var store = _store;
            _store = null;
            if (store != null) { try { store.Dispose(); } catch { } }
        }
    }
}
