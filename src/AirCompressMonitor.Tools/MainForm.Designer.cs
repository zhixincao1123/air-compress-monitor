namespace AirCompressMonitor.Tools
{
    // 四个参数条（panelScan / panelMon / panelCollect / panelDiag）用的是**绝对坐标**，
    // 不是 FlowLayoutPanel。
    //
    // 为什么：这些条子是按「两行、每行几个控件」排好的，位置就是按像素定的。
    // 一开始用的是 FlowLayoutPanel，结果它按自己的规则重排 —— Location 被忽略，
    // 内容在宽度处换行，本该在第二行的控件被挤出去、右边那几个直接看不见了。
    // 用 Panel 则完全按写好的坐标走，所见即所得。
    //
    // 代价是窗口窄到一定程度会把右边的控件裁掉，所以窗体 MinimumSize 卡在 900 宽 ——
    // 984（设计宽度）减去边框和内边距后还剩 870，最靠右的控件到 823，留了余量。
    //
    // 关于高 DPI：本程序刻意**不**声明 DPI 感知（没有 app.manifest）。
    // 于是 Windows 在 200% 缩放的屏上会把整个窗口按位图放大 —— 字发虚，但版面比例是对的。
    // 反过来，一旦声明 DPI 感知，WinForms 会按字体比例缩放绝对坐标，
    // panelScan 那行 823 像素宽的控件会被拉到 1646，直接撑破 984 的窗口。
    // 要既清晰又不裁，得把四个参数条改成 TableLayoutPanel 之类的自适应容器 ——
    // 那是另一件事，先按「版面正确优先」处理。
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.tabs = new System.Windows.Forms.TabControl();
            this.tabScan = new System.Windows.Forms.TabPage();
            this.dgvScan = new System.Windows.Forms.DataGridView();
            this.panelScan = new System.Windows.Forms.Panel();
            this.lblScanPort = new System.Windows.Forms.Label();
            this.cboScanPort = new System.Windows.Forms.ComboBox();
            this.btnScanRefreshPorts = new System.Windows.Forms.Button();
            this.lblScanBaud = new System.Windows.Forms.Label();
            this.cboScanBaud = new System.Windows.Forms.ComboBox();
            this.lblScanFrom = new System.Windows.Forms.Label();
            this.numScanFrom = new System.Windows.Forms.NumericUpDown();
            this.lblScanTo = new System.Windows.Forms.Label();
            this.numScanTo = new System.Windows.Forms.NumericUpDown();
            this.lblScanFunc = new System.Windows.Forms.Label();
            this.cboScanFunc = new System.Windows.Forms.ComboBox();
            this.btnScanStart = new System.Windows.Forms.Button();
            this.btnScanStop = new System.Windows.Forms.Button();
            this.lblScanStatus = new System.Windows.Forms.Label();
            this.tabMonitor = new System.Windows.Forms.TabPage();
            this.dgvMon = new System.Windows.Forms.DataGridView();
            this.panelMon = new System.Windows.Forms.Panel();
            this.lblBrand = new System.Windows.Forms.Label();
            this.cboBrand = new System.Windows.Forms.ComboBox();
            this.btnApplyBrand = new System.Windows.Forms.Button();
            this.lblMonSlave = new System.Windows.Forms.Label();
            this.numMonSlave = new System.Windows.Forms.NumericUpDown();
            this.lblMonStart = new System.Windows.Forms.Label();
            this.numMonStart = new System.Windows.Forms.NumericUpDown();
            this.lblMonCount = new System.Windows.Forms.Label();
            this.numMonCount = new System.Windows.Forms.NumericUpDown();
            this.lblMonFunc = new System.Windows.Forms.Label();
            this.cboMonFunc = new System.Windows.Forms.ComboBox();
            this.btnReadOnce = new System.Windows.Forms.Button();
            this.chkAutoPoll = new System.Windows.Forms.CheckBox();
            this.lblPollMs = new System.Windows.Forms.Label();
            this.numPollMs = new System.Windows.Forms.NumericUpDown();
            this.lblMonLink = new System.Windows.Forms.Label();
            this.lblMonStatus = new System.Windows.Forms.Label();
            this.lblBrandNote = new System.Windows.Forms.Label();
            this.tabCollect = new System.Windows.Forms.TabPage();
            this.dgvCollect = new System.Windows.Forms.DataGridView();
            this.txtCollectLog = new System.Windows.Forms.TextBox();
            this.panelCollect = new System.Windows.Forms.Panel();
            this.lblStoreSlave = new System.Windows.Forms.Label();
            this.numStoreSlave = new System.Windows.Forms.NumericUpDown();
            this.lblStoreStart = new System.Windows.Forms.Label();
            this.numStoreStart = new System.Windows.Forms.NumericUpDown();
            this.lblStoreCount = new System.Windows.Forms.Label();
            this.numStoreCount = new System.Windows.Forms.NumericUpDown();
            this.lblStoreMode = new System.Windows.Forms.Label();
            this.cboStoreMode = new System.Windows.Forms.ComboBox();
            this.lblCollectLink = new System.Windows.Forms.Label();
            this.btnCollectStart = new System.Windows.Forms.Button();
            this.btnCollectStop = new System.Windows.Forms.Button();
            this.lblCollectStatus = new System.Windows.Forms.Label();
            this.tabDiag = new System.Windows.Forms.TabPage();
            this.dgvDiag = new System.Windows.Forms.DataGridView();
            this.panelDiag = new System.Windows.Forms.Panel();
            this.btnDiagClear = new System.Windows.Forms.Button();
            this.btnDiagExport = new System.Windows.Forms.Button();
            this.lblDiagCount = new System.Windows.Forms.Label();
            this.tabs.SuspendLayout();
            this.tabScan.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvScan)).BeginInit();
            this.panelScan.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numScanFrom)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numScanTo)).BeginInit();
            this.tabMonitor.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvMon)).BeginInit();
            this.panelMon.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numMonSlave)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMonStart)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMonCount)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPollMs)).BeginInit();
            this.tabCollect.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvCollect)).BeginInit();
            this.panelCollect.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numStoreSlave)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numStoreStart)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numStoreCount)).BeginInit();
            this.tabDiag.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvDiag)).BeginInit();
            this.panelDiag.SuspendLayout();
            this.SuspendLayout();
            //
            // tabs
            //
            this.tabs.Controls.Add(this.tabScan);
            this.tabs.Controls.Add(this.tabMonitor);
            this.tabs.Controls.Add(this.tabCollect);
            this.tabs.Controls.Add(this.tabDiag);
            this.tabs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabs.Location = new System.Drawing.Point(0, 0);
            this.tabs.Name = "tabs";
            this.tabs.SelectedIndex = 0;
            this.tabs.Size = new System.Drawing.Size(984, 641);
            this.tabs.TabIndex = 0;
            //
            // tabScan
            //
            this.tabScan.Controls.Add(this.dgvScan);
            this.tabScan.Controls.Add(this.panelScan);
            this.tabScan.Location = new System.Drawing.Point(4, 22);
            this.tabScan.Name = "tabScan";
            this.tabScan.Padding = new System.Windows.Forms.Padding(3);
            this.tabScan.Size = new System.Drawing.Size(976, 615);
            this.tabScan.TabIndex = 0;
            this.tabScan.Text = "从站扫描";
            this.tabScan.UseVisualStyleBackColor = true;
            //
            // panelScan
            //
            this.panelScan.Controls.Add(this.lblScanPort);
            this.panelScan.Controls.Add(this.cboScanPort);
            this.panelScan.Controls.Add(this.btnScanRefreshPorts);
            this.panelScan.Controls.Add(this.lblScanBaud);
            this.panelScan.Controls.Add(this.cboScanBaud);
            this.panelScan.Controls.Add(this.lblScanFrom);
            this.panelScan.Controls.Add(this.numScanFrom);
            this.panelScan.Controls.Add(this.lblScanTo);
            this.panelScan.Controls.Add(this.numScanTo);
            this.panelScan.Controls.Add(this.lblScanFunc);
            this.panelScan.Controls.Add(this.cboScanFunc);
            this.panelScan.Controls.Add(this.btnScanStart);
            this.panelScan.Controls.Add(this.btnScanStop);
            this.panelScan.Controls.Add(this.lblScanStatus);
            this.panelScan.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelScan.Location = new System.Drawing.Point(3, 3);
            this.panelScan.Name = "panelScan";
            this.panelScan.Padding = new System.Windows.Forms.Padding(6);
            this.panelScan.Size = new System.Drawing.Size(970, 76);
            this.panelScan.TabIndex = 1;
            //
            // lblScanPort
            //
            this.lblScanPort.AutoSize = true;
            this.lblScanPort.Location = new System.Drawing.Point(9, 12);
            this.lblScanPort.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblScanPort.Name = "lblScanPort";
            this.lblScanPort.Size = new System.Drawing.Size(41, 12);
            this.lblScanPort.TabIndex = 0;
            this.lblScanPort.Text = "串口号";
            //
            // cboScanPort
            //
            this.cboScanPort.FormattingEnabled = true;
            this.cboScanPort.Location = new System.Drawing.Point(56, 9);
            this.cboScanPort.Name = "cboScanPort";
            this.cboScanPort.Size = new System.Drawing.Size(110, 20);
            this.cboScanPort.TabIndex = 1;
            //
            // btnScanRefreshPorts
            //
            this.btnScanRefreshPorts.Location = new System.Drawing.Point(172, 8);
            this.btnScanRefreshPorts.Name = "btnScanRefreshPorts";
            this.btnScanRefreshPorts.Size = new System.Drawing.Size(55, 23);
            this.btnScanRefreshPorts.TabIndex = 2;
            this.btnScanRefreshPorts.Text = "刷新";
            this.btnScanRefreshPorts.UseVisualStyleBackColor = true;
            //
            // lblScanBaud
            //
            this.lblScanBaud.AutoSize = true;
            this.lblScanBaud.Location = new System.Drawing.Point(233, 12);
            this.lblScanBaud.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblScanBaud.Name = "lblScanBaud";
            this.lblScanBaud.Size = new System.Drawing.Size(41, 12);
            this.lblScanBaud.TabIndex = 3;
            this.lblScanBaud.Text = "波特率";
            //
            // cboScanBaud
            //
            this.cboScanBaud.FormattingEnabled = true;
            this.cboScanBaud.Location = new System.Drawing.Point(280, 9);
            this.cboScanBaud.Name = "cboScanBaud";
            this.cboScanBaud.Size = new System.Drawing.Size(90, 20);
            this.cboScanBaud.TabIndex = 4;
            //
            // lblScanFrom
            //
            this.lblScanFrom.AutoSize = true;
            this.lblScanFrom.Location = new System.Drawing.Point(376, 12);
            this.lblScanFrom.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblScanFrom.Name = "lblScanFrom";
            this.lblScanFrom.Size = new System.Drawing.Size(53, 12);
            this.lblScanFrom.TabIndex = 5;
            this.lblScanFrom.Text = "起始地址";
            //
            // numScanFrom
            //
            this.numScanFrom.Location = new System.Drawing.Point(435, 9);
            this.numScanFrom.Maximum = new decimal(new int[] { 247, 0, 0, 0 });
            this.numScanFrom.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numScanFrom.Name = "numScanFrom";
            this.numScanFrom.Size = new System.Drawing.Size(60, 21);
            this.numScanFrom.TabIndex = 6;
            this.numScanFrom.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // lblScanTo
            //
            this.lblScanTo.AutoSize = true;
            this.lblScanTo.Location = new System.Drawing.Point(501, 12);
            this.lblScanTo.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblScanTo.Name = "lblScanTo";
            this.lblScanTo.Size = new System.Drawing.Size(53, 12);
            this.lblScanTo.TabIndex = 7;
            this.lblScanTo.Text = "结束地址";
            //
            // numScanTo
            //
            this.numScanTo.Location = new System.Drawing.Point(560, 9);
            this.numScanTo.Maximum = new decimal(new int[] { 247, 0, 0, 0 });
            this.numScanTo.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numScanTo.Name = "numScanTo";
            this.numScanTo.Size = new System.Drawing.Size(60, 21);
            this.numScanTo.TabIndex = 8;
            this.numScanTo.Value = new decimal(new int[] { 16, 0, 0, 0 });
            //
            // lblScanFunc
            //
            this.lblScanFunc.AutoSize = true;
            this.lblScanFunc.Location = new System.Drawing.Point(626, 12);
            this.lblScanFunc.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblScanFunc.Name = "lblScanFunc";
            this.lblScanFunc.Size = new System.Drawing.Size(41, 12);
            this.lblScanFunc.TabIndex = 9;
            this.lblScanFunc.Text = "功能码";
            //
            // cboScanFunc
            //
            this.cboScanFunc.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboScanFunc.FormattingEnabled = true;
            this.cboScanFunc.Location = new System.Drawing.Point(673, 9);
            this.cboScanFunc.Name = "cboScanFunc";
            this.cboScanFunc.Size = new System.Drawing.Size(150, 20);
            this.cboScanFunc.TabIndex = 10;
            //
            // btnScanStart
            //
            this.btnScanStart.Location = new System.Drawing.Point(9, 41);
            this.btnScanStart.Name = "btnScanStart";
            this.btnScanStart.Size = new System.Drawing.Size(90, 25);
            this.btnScanStart.TabIndex = 11;
            this.btnScanStart.Text = "开始扫描";
            this.btnScanStart.UseVisualStyleBackColor = true;
            //
            // btnScanStop
            //
            this.btnScanStop.Enabled = false;
            this.btnScanStop.Location = new System.Drawing.Point(105, 41);
            this.btnScanStop.Name = "btnScanStop";
            this.btnScanStop.Size = new System.Drawing.Size(75, 25);
            this.btnScanStop.TabIndex = 12;
            this.btnScanStop.Text = "停止";
            this.btnScanStop.UseVisualStyleBackColor = true;
            //
            // lblScanStatus
            //
            this.lblScanStatus.AutoSize = true;
            this.lblScanStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(44)))), ((int)(((byte)(126)))), ((int)(((byte)(219)))));
            this.lblScanStatus.Location = new System.Drawing.Point(189, 47);
            this.lblScanStatus.Margin = new System.Windows.Forms.Padding(9, 6, 3, 0);
            this.lblScanStatus.Name = "lblScanStatus";
            this.lblScanStatus.Size = new System.Drawing.Size(41, 12);
            this.lblScanStatus.TabIndex = 13;
            this.lblScanStatus.Text = "就绪";
            //
            // dgvScan
            //
            this.dgvScan.AllowUserToAddRows = false;
            this.dgvScan.AllowUserToDeleteRows = false;
            this.dgvScan.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvScan.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvScan.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvScan.Location = new System.Drawing.Point(3, 79);
            this.dgvScan.Name = "dgvScan";
            this.dgvScan.ReadOnly = true;
            this.dgvScan.RowHeadersVisible = false;
            this.dgvScan.RowTemplate.Height = 23;
            this.dgvScan.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvScan.Size = new System.Drawing.Size(970, 533);
            this.dgvScan.TabIndex = 0;
            //
            // tabMonitor
            //
            this.tabMonitor.Controls.Add(this.dgvMon);
            this.tabMonitor.Controls.Add(this.panelMon);
            this.tabMonitor.Location = new System.Drawing.Point(4, 22);
            this.tabMonitor.Name = "tabMonitor";
            this.tabMonitor.Padding = new System.Windows.Forms.Padding(3);
            this.tabMonitor.Size = new System.Drawing.Size(976, 615);
            this.tabMonitor.TabIndex = 1;
            this.tabMonitor.Text = "寄存器监视";
            this.tabMonitor.UseVisualStyleBackColor = true;
            //
            // panelMon
            //
            this.panelMon.Controls.Add(this.lblBrand);
            this.panelMon.Controls.Add(this.cboBrand);
            this.panelMon.Controls.Add(this.btnApplyBrand);
            this.panelMon.Controls.Add(this.lblMonSlave);
            this.panelMon.Controls.Add(this.numMonSlave);
            this.panelMon.Controls.Add(this.lblMonStart);
            this.panelMon.Controls.Add(this.numMonStart);
            this.panelMon.Controls.Add(this.lblMonCount);
            this.panelMon.Controls.Add(this.numMonCount);
            this.panelMon.Controls.Add(this.lblMonFunc);
            this.panelMon.Controls.Add(this.cboMonFunc);
            this.panelMon.Controls.Add(this.btnReadOnce);
            this.panelMon.Controls.Add(this.chkAutoPoll);
            this.panelMon.Controls.Add(this.lblPollMs);
            this.panelMon.Controls.Add(this.numPollMs);
            this.panelMon.Controls.Add(this.lblMonLink);
            this.panelMon.Controls.Add(this.lblMonStatus);
            this.panelMon.Controls.Add(this.lblBrandNote);
            this.panelMon.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelMon.Location = new System.Drawing.Point(3, 3);
            this.panelMon.Name = "panelMon";
            this.panelMon.Padding = new System.Windows.Forms.Padding(6);
            this.panelMon.Size = new System.Drawing.Size(970, 104);
            this.panelMon.TabIndex = 1;
            //
            // lblBrand
            //
            this.lblBrand.AutoSize = true;
            this.lblBrand.Location = new System.Drawing.Point(9, 12);
            this.lblBrand.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblBrand.Name = "lblBrand";
            this.lblBrand.Size = new System.Drawing.Size(53, 12);
            this.lblBrand.TabIndex = 0;
            this.lblBrand.Text = "品牌模板";
            //
            // cboBrand
            //
            this.cboBrand.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboBrand.FormattingEnabled = true;
            this.cboBrand.Location = new System.Drawing.Point(68, 9);
            this.cboBrand.Name = "cboBrand";
            this.cboBrand.Size = new System.Drawing.Size(200, 20);
            this.cboBrand.TabIndex = 1;
            //
            // btnApplyBrand
            //
            this.btnApplyBrand.Location = new System.Drawing.Point(274, 8);
            this.btnApplyBrand.Name = "btnApplyBrand";
            this.btnApplyBrand.Size = new System.Drawing.Size(85, 23);
            this.btnApplyBrand.TabIndex = 2;
            this.btnApplyBrand.Text = "应用模板";
            this.btnApplyBrand.UseVisualStyleBackColor = true;
            //
            // lblMonSlave
            //
            this.lblMonSlave.AutoSize = true;
            this.lblMonSlave.Location = new System.Drawing.Point(371, 12);
            this.lblMonSlave.Margin = new System.Windows.Forms.Padding(9, 6, 3, 0);
            this.lblMonSlave.Name = "lblMonSlave";
            this.lblMonSlave.Size = new System.Drawing.Size(41, 12);
            this.lblMonSlave.TabIndex = 3;
            this.lblMonSlave.Text = "从站号";
            //
            // numMonSlave
            //
            this.numMonSlave.Location = new System.Drawing.Point(418, 9);
            this.numMonSlave.Maximum = new decimal(new int[] { 247, 0, 0, 0 });
            this.numMonSlave.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numMonSlave.Name = "numMonSlave";
            this.numMonSlave.Size = new System.Drawing.Size(55, 21);
            this.numMonSlave.TabIndex = 4;
            this.numMonSlave.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // lblMonStart
            //
            this.lblMonStart.AutoSize = true;
            this.lblMonStart.Location = new System.Drawing.Point(479, 12);
            this.lblMonStart.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblMonStart.Name = "lblMonStart";
            this.lblMonStart.Size = new System.Drawing.Size(53, 12);
            this.lblMonStart.TabIndex = 5;
            this.lblMonStart.Text = "起始地址";
            //
            // numMonStart
            //
            this.numMonStart.Location = new System.Drawing.Point(538, 9);
            this.numMonStart.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numMonStart.Name = "numMonStart";
            this.numMonStart.Size = new System.Drawing.Size(70, 21);
            this.numMonStart.TabIndex = 6;
            //
            // lblMonCount
            //
            this.lblMonCount.AutoSize = true;
            this.lblMonCount.Location = new System.Drawing.Point(614, 12);
            this.lblMonCount.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblMonCount.Name = "lblMonCount";
            this.lblMonCount.Size = new System.Drawing.Size(53, 12);
            this.lblMonCount.TabIndex = 7;
            this.lblMonCount.Text = "寄存器数";
            //
            // numMonCount
            //
            this.numMonCount.Location = new System.Drawing.Point(673, 9);
            this.numMonCount.Maximum = new decimal(new int[] { 125, 0, 0, 0 });
            this.numMonCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numMonCount.Name = "numMonCount";
            this.numMonCount.Size = new System.Drawing.Size(55, 21);
            this.numMonCount.TabIndex = 8;
            this.numMonCount.Value = new decimal(new int[] { 5, 0, 0, 0 });
            //
            // lblMonFunc
            //
            this.lblMonFunc.AutoSize = true;
            this.lblMonFunc.Location = new System.Drawing.Point(734, 12);
            this.lblMonFunc.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblMonFunc.Name = "lblMonFunc";
            this.lblMonFunc.Size = new System.Drawing.Size(41, 12);
            this.lblMonFunc.TabIndex = 9;
            this.lblMonFunc.Text = "功能码";
            //
            // cboMonFunc
            //
            this.cboMonFunc.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboMonFunc.FormattingEnabled = true;
            this.cboMonFunc.Location = new System.Drawing.Point(781, 9);
            this.cboMonFunc.Name = "cboMonFunc";
            this.cboMonFunc.Size = new System.Drawing.Size(160, 20);
            this.cboMonFunc.TabIndex = 10;
            //
            // btnReadOnce
            //
            this.btnReadOnce.Location = new System.Drawing.Point(9, 39);
            this.btnReadOnce.Name = "btnReadOnce";
            this.btnReadOnce.Size = new System.Drawing.Size(85, 25);
            this.btnReadOnce.TabIndex = 11;
            this.btnReadOnce.Text = "读取一次";
            this.btnReadOnce.UseVisualStyleBackColor = true;
            //
            // chkAutoPoll
            //
            this.chkAutoPoll.AutoSize = true;
            this.chkAutoPoll.Location = new System.Drawing.Point(100, 44);
            this.chkAutoPoll.Name = "chkAutoPoll";
            this.chkAutoPoll.Size = new System.Drawing.Size(72, 16);
            this.chkAutoPoll.TabIndex = 12;
            this.chkAutoPoll.Text = "自动轮询";
            this.chkAutoPoll.UseVisualStyleBackColor = true;
            //
            // lblPollMs
            //
            this.lblPollMs.AutoSize = true;
            this.lblPollMs.Location = new System.Drawing.Point(178, 46);
            this.lblPollMs.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblPollMs.Name = "lblPollMs";
            this.lblPollMs.Size = new System.Drawing.Size(65, 12);
            this.lblPollMs.TabIndex = 13;
            this.lblPollMs.Text = "周期(毫秒)";
            //
            // numPollMs
            //
            this.numPollMs.Increment = new decimal(new int[] { 100, 0, 0, 0 });
            this.numPollMs.Location = new System.Drawing.Point(249, 42);
            this.numPollMs.Maximum = new decimal(new int[] { 60000, 0, 0, 0 });
            this.numPollMs.Minimum = new decimal(new int[] { 200, 0, 0, 0 });
            this.numPollMs.Name = "numPollMs";
            this.numPollMs.Size = new System.Drawing.Size(70, 21);
            this.numPollMs.TabIndex = 14;
            this.numPollMs.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            //
            // lblMonLink
            //
            this.lblMonLink.AutoSize = true;
            this.lblMonLink.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(102)))), ((int)(((byte)(102)))), ((int)(((byte)(102)))));
            this.lblMonLink.Location = new System.Drawing.Point(325, 46);
            this.lblMonLink.Margin = new System.Windows.Forms.Padding(9, 6, 3, 0);
            this.lblMonLink.Name = "lblMonLink";
            this.lblMonLink.Size = new System.Drawing.Size(41, 12);
            this.lblMonLink.TabIndex = 16;
            this.lblMonLink.Text = "链路：未选择";
            //
            // lblMonStatus
            //
            this.lblMonStatus.AutoSize = true;
            this.lblMonStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(44)))), ((int)(((byte)(126)))), ((int)(((byte)(219)))));
            this.lblMonStatus.Location = new System.Drawing.Point(600, 46);
            this.lblMonStatus.Margin = new System.Windows.Forms.Padding(9, 6, 3, 0);
            this.lblMonStatus.Name = "lblMonStatus";
            this.lblMonStatus.Size = new System.Drawing.Size(41, 12);
            this.lblMonStatus.TabIndex = 17;
            this.lblMonStatus.Text = "就绪";
            //
            // lblBrandNote
            //
            this.lblBrandNote.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(102)))), ((int)(((byte)(102)))), ((int)(((byte)(102)))));
            this.lblBrandNote.Location = new System.Drawing.Point(9, 70);
            this.lblBrandNote.Name = "lblBrandNote";
            this.lblBrandNote.Size = new System.Drawing.Size(940, 30);
            this.lblBrandNote.TabIndex = 15;
            this.lblBrandNote.Text = "选择品牌模板后点「应用模板」，地址/数量/字节序/换算系数会一次性填好。";
            //
            // dgvMon
            //
            this.dgvMon.AllowUserToAddRows = false;
            this.dgvMon.AllowUserToDeleteRows = false;
            this.dgvMon.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvMon.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvMon.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvMon.Location = new System.Drawing.Point(3, 107);
            this.dgvMon.Name = "dgvMon";
            this.dgvMon.ReadOnly = true;
            this.dgvMon.RowHeadersVisible = false;
            this.dgvMon.RowTemplate.Height = 23;
            this.dgvMon.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvMon.Size = new System.Drawing.Size(970, 505);
            this.dgvMon.TabIndex = 0;
            //
            // tabCollect
            //
            this.tabCollect.Controls.Add(this.dgvCollect);
            this.tabCollect.Controls.Add(this.txtCollectLog);
            this.tabCollect.Controls.Add(this.panelCollect);
            this.tabCollect.Location = new System.Drawing.Point(4, 22);
            this.tabCollect.Name = "tabCollect";
            this.tabCollect.Padding = new System.Windows.Forms.Padding(3);
            this.tabCollect.Size = new System.Drawing.Size(976, 615);
            this.tabCollect.TabIndex = 2;
            this.tabCollect.Text = "采集与落盘";
            this.tabCollect.UseVisualStyleBackColor = true;
            //
            // panelCollect
            //
            this.panelCollect.Controls.Add(this.lblStoreSlave);
            this.panelCollect.Controls.Add(this.numStoreSlave);
            this.panelCollect.Controls.Add(this.lblStoreStart);
            this.panelCollect.Controls.Add(this.numStoreStart);
            this.panelCollect.Controls.Add(this.lblStoreCount);
            this.panelCollect.Controls.Add(this.numStoreCount);
            this.panelCollect.Controls.Add(this.lblStoreMode);
            this.panelCollect.Controls.Add(this.cboStoreMode);
            this.panelCollect.Controls.Add(this.lblCollectLink);
            this.panelCollect.Controls.Add(this.btnCollectStart);
            this.panelCollect.Controls.Add(this.btnCollectStop);
            this.panelCollect.Controls.Add(this.lblCollectStatus);
            this.panelCollect.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelCollect.Location = new System.Drawing.Point(3, 3);
            this.panelCollect.Name = "panelCollect";
            this.panelCollect.Padding = new System.Windows.Forms.Padding(6);
            this.panelCollect.Size = new System.Drawing.Size(970, 76);
            this.panelCollect.TabIndex = 2;
            //
            // lblStoreSlave
            //
            this.lblStoreSlave.AutoSize = true;
            this.lblStoreSlave.Location = new System.Drawing.Point(9, 12);
            this.lblStoreSlave.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblStoreSlave.Name = "lblStoreSlave";
            this.lblStoreSlave.Size = new System.Drawing.Size(41, 12);
            this.lblStoreSlave.TabIndex = 0;
            this.lblStoreSlave.Text = "从站号";
            //
            // numStoreSlave
            //
            this.numStoreSlave.Location = new System.Drawing.Point(56, 9);
            this.numStoreSlave.Maximum = new decimal(new int[] { 247, 0, 0, 0 });
            this.numStoreSlave.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numStoreSlave.Name = "numStoreSlave";
            this.numStoreSlave.Size = new System.Drawing.Size(55, 21);
            this.numStoreSlave.TabIndex = 1;
            this.numStoreSlave.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // lblStoreStart
            //
            this.lblStoreStart.AutoSize = true;
            this.lblStoreStart.Location = new System.Drawing.Point(117, 12);
            this.lblStoreStart.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblStoreStart.Name = "lblStoreStart";
            this.lblStoreStart.Size = new System.Drawing.Size(53, 12);
            this.lblStoreStart.TabIndex = 2;
            this.lblStoreStart.Text = "起始地址";
            //
            // numStoreStart
            //
            this.numStoreStart.Location = new System.Drawing.Point(176, 9);
            this.numStoreStart.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numStoreStart.Name = "numStoreStart";
            this.numStoreStart.Size = new System.Drawing.Size(70, 21);
            this.numStoreStart.TabIndex = 3;
            //
            // lblStoreCount
            //
            this.lblStoreCount.AutoSize = true;
            this.lblStoreCount.Location = new System.Drawing.Point(252, 12);
            this.lblStoreCount.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblStoreCount.Name = "lblStoreCount";
            this.lblStoreCount.Size = new System.Drawing.Size(53, 12);
            this.lblStoreCount.TabIndex = 4;
            this.lblStoreCount.Text = "寄存器数";
            //
            // numStoreCount
            //
            this.numStoreCount.Location = new System.Drawing.Point(311, 9);
            this.numStoreCount.Maximum = new decimal(new int[] { 125, 0, 0, 0 });
            this.numStoreCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numStoreCount.Name = "numStoreCount";
            this.numStoreCount.Size = new System.Drawing.Size(55, 21);
            this.numStoreCount.TabIndex = 5;
            this.numStoreCount.Value = new decimal(new int[] { 5, 0, 0, 0 });
            //
            // lblStoreMode
            //
            this.lblStoreMode.AutoSize = true;
            this.lblStoreMode.Location = new System.Drawing.Point(372, 12);
            this.lblStoreMode.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblStoreMode.Name = "lblStoreMode";
            this.lblStoreMode.Size = new System.Drawing.Size(53, 12);
            this.lblStoreMode.TabIndex = 6;
            this.lblStoreMode.Text = "存储模式";
            //
            // cboStoreMode
            //
            this.cboStoreMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboStoreMode.FormattingEnabled = true;
            this.cboStoreMode.Location = new System.Drawing.Point(431, 9);
            this.cboStoreMode.Name = "cboStoreMode";
            this.cboStoreMode.Size = new System.Drawing.Size(170, 20);
            this.cboStoreMode.TabIndex = 7;
            //
            // lblCollectLink
            //
            this.lblCollectLink.AutoSize = true;
            this.lblCollectLink.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(102)))), ((int)(((byte)(102)))), ((int)(((byte)(102)))));
            this.lblCollectLink.Location = new System.Drawing.Point(607, 12);
            this.lblCollectLink.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.lblCollectLink.Name = "lblCollectLink";
            this.lblCollectLink.Size = new System.Drawing.Size(41, 12);
            this.lblCollectLink.TabIndex = 11;
            this.lblCollectLink.Text = "链路：未选择";
            //
            // btnCollectStart
            //
            this.btnCollectStart.Location = new System.Drawing.Point(9, 41);
            this.btnCollectStart.Name = "btnCollectStart";
            this.btnCollectStart.Size = new System.Drawing.Size(90, 25);
            this.btnCollectStart.TabIndex = 8;
            this.btnCollectStart.Text = "开始采集";
            this.btnCollectStart.UseVisualStyleBackColor = true;
            //
            // btnCollectStop
            //
            this.btnCollectStop.Enabled = false;
            this.btnCollectStop.Location = new System.Drawing.Point(105, 41);
            this.btnCollectStop.Name = "btnCollectStop";
            this.btnCollectStop.Size = new System.Drawing.Size(90, 25);
            this.btnCollectStop.TabIndex = 9;
            this.btnCollectStop.Text = "停止采集";
            this.btnCollectStop.UseVisualStyleBackColor = true;
            //
            // lblCollectStatus
            //
            this.lblCollectStatus.AutoSize = true;
            this.lblCollectStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(44)))), ((int)(((byte)(126)))), ((int)(((byte)(219)))));
            this.lblCollectStatus.Location = new System.Drawing.Point(207, 47);
            this.lblCollectStatus.Margin = new System.Windows.Forms.Padding(9, 6, 3, 0);
            this.lblCollectStatus.Name = "lblCollectStatus";
            this.lblCollectStatus.Size = new System.Drawing.Size(41, 12);
            this.lblCollectStatus.TabIndex = 10;
            this.lblCollectStatus.Text = "未开始";
            //
            // dgvCollect
            //
            this.dgvCollect.AllowUserToAddRows = false;
            this.dgvCollect.AllowUserToDeleteRows = false;
            this.dgvCollect.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvCollect.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvCollect.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvCollect.Location = new System.Drawing.Point(3, 79);
            this.dgvCollect.Name = "dgvCollect";
            this.dgvCollect.ReadOnly = true;
            this.dgvCollect.RowHeadersVisible = false;
            this.dgvCollect.RowTemplate.Height = 23;
            this.dgvCollect.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvCollect.Size = new System.Drawing.Size(970, 413);
            this.dgvCollect.TabIndex = 0;
            //
            // txtCollectLog
            //
            this.txtCollectLog.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.txtCollectLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtCollectLog.Location = new System.Drawing.Point(3, 492);
            this.txtCollectLog.Multiline = true;
            this.txtCollectLog.Name = "txtCollectLog";
            this.txtCollectLog.ReadOnly = true;
            this.txtCollectLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtCollectLog.Size = new System.Drawing.Size(970, 120);
            this.txtCollectLog.TabIndex = 1;
            //
            // tabDiag
            //
            this.tabDiag.Controls.Add(this.dgvDiag);
            this.tabDiag.Controls.Add(this.panelDiag);
            this.tabDiag.Location = new System.Drawing.Point(4, 22);
            this.tabDiag.Name = "tabDiag";
            this.tabDiag.Padding = new System.Windows.Forms.Padding(3);
            this.tabDiag.Size = new System.Drawing.Size(976, 615);
            this.tabDiag.TabIndex = 3;
            this.tabDiag.Text = "通信诊断";
            this.tabDiag.UseVisualStyleBackColor = true;
            //
            // panelDiag
            //
            this.panelDiag.Controls.Add(this.btnDiagClear);
            this.panelDiag.Controls.Add(this.btnDiagExport);
            this.panelDiag.Controls.Add(this.lblDiagCount);
            this.panelDiag.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelDiag.Location = new System.Drawing.Point(3, 556);
            this.panelDiag.Name = "panelDiag";
            this.panelDiag.Padding = new System.Windows.Forms.Padding(6);
            this.panelDiag.Size = new System.Drawing.Size(970, 56);
            this.panelDiag.TabIndex = 1;
            //
            // btnDiagClear
            //
            this.btnDiagClear.Location = new System.Drawing.Point(9, 9);
            this.btnDiagClear.Name = "btnDiagClear";
            this.btnDiagClear.Size = new System.Drawing.Size(90, 25);
            this.btnDiagClear.TabIndex = 0;
            this.btnDiagClear.Text = "清除记录";
            this.btnDiagClear.UseVisualStyleBackColor = true;
            //
            // btnDiagExport
            //
            this.btnDiagExport.Location = new System.Drawing.Point(105, 9);
            this.btnDiagExport.Name = "btnDiagExport";
            this.btnDiagExport.Size = new System.Drawing.Size(90, 25);
            this.btnDiagExport.TabIndex = 1;
            this.btnDiagExport.Text = "导出 CSV";
            this.btnDiagExport.UseVisualStyleBackColor = true;
            //
            // lblDiagCount
            //
            this.lblDiagCount.AutoSize = true;
            this.lblDiagCount.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(102)))), ((int)(((byte)(102)))), ((int)(((byte)(102)))));
            this.lblDiagCount.Location = new System.Drawing.Point(207, 15);
            this.lblDiagCount.Margin = new System.Windows.Forms.Padding(9, 6, 3, 0);
            this.lblDiagCount.Name = "lblDiagCount";
            this.lblDiagCount.Size = new System.Drawing.Size(41, 12);
            this.lblDiagCount.TabIndex = 2;
            this.lblDiagCount.Text = "共 0 条";
            //
            // dgvDiag
            //
            this.dgvDiag.AllowUserToAddRows = false;
            this.dgvDiag.AllowUserToDeleteRows = false;
            this.dgvDiag.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvDiag.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvDiag.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvDiag.Location = new System.Drawing.Point(3, 3);
            this.dgvDiag.Name = "dgvDiag";
            this.dgvDiag.ReadOnly = true;
            this.dgvDiag.RowHeadersVisible = false;
            this.dgvDiag.RowTemplate.Height = 23;
            this.dgvDiag.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvDiag.Size = new System.Drawing.Size(970, 553);
            this.dgvDiag.TabIndex = 0;
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(984, 641);
            this.Controls.Add(this.tabs);
            this.MinimumSize = new System.Drawing.Size(900, 560);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "工业设备监控工具集 — Modbus RTU/TCP";
            this.tabs.ResumeLayout(false);
            this.tabScan.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvScan)).EndInit();
            this.panelScan.ResumeLayout(false);
            this.panelScan.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numScanFrom)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numScanTo)).EndInit();
            this.tabMonitor.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvMon)).EndInit();
            this.panelMon.ResumeLayout(false);
            this.panelMon.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numMonSlave)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMonStart)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMonCount)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPollMs)).EndInit();
            this.tabCollect.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvCollect)).EndInit();
            this.panelCollect.ResumeLayout(false);
            this.panelCollect.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numStoreSlave)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numStoreStart)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numStoreCount)).EndInit();
            this.tabDiag.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvDiag)).EndInit();
            this.panelDiag.ResumeLayout(false);
            this.panelDiag.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TabControl tabs;
        private System.Windows.Forms.TabPage tabScan;
        private System.Windows.Forms.DataGridView dgvScan;
        private System.Windows.Forms.Panel panelScan;
        private System.Windows.Forms.Label lblScanPort;
        private System.Windows.Forms.ComboBox cboScanPort;
        private System.Windows.Forms.Button btnScanRefreshPorts;
        private System.Windows.Forms.Label lblScanBaud;
        private System.Windows.Forms.ComboBox cboScanBaud;
        private System.Windows.Forms.Label lblScanFrom;
        private System.Windows.Forms.NumericUpDown numScanFrom;
        private System.Windows.Forms.Label lblScanTo;
        private System.Windows.Forms.NumericUpDown numScanTo;
        private System.Windows.Forms.Label lblScanFunc;
        private System.Windows.Forms.ComboBox cboScanFunc;
        private System.Windows.Forms.Button btnScanStart;
        private System.Windows.Forms.Button btnScanStop;
        private System.Windows.Forms.Label lblScanStatus;
        private System.Windows.Forms.TabPage tabMonitor;
        private System.Windows.Forms.DataGridView dgvMon;
        private System.Windows.Forms.Panel panelMon;
        private System.Windows.Forms.Label lblBrand;
        private System.Windows.Forms.ComboBox cboBrand;
        private System.Windows.Forms.Button btnApplyBrand;
        private System.Windows.Forms.Label lblMonSlave;
        private System.Windows.Forms.NumericUpDown numMonSlave;
        private System.Windows.Forms.Label lblMonStart;
        private System.Windows.Forms.NumericUpDown numMonStart;
        private System.Windows.Forms.Label lblMonCount;
        private System.Windows.Forms.NumericUpDown numMonCount;
        private System.Windows.Forms.Label lblMonFunc;
        private System.Windows.Forms.ComboBox cboMonFunc;
        private System.Windows.Forms.Button btnReadOnce;
        private System.Windows.Forms.CheckBox chkAutoPoll;
        private System.Windows.Forms.Label lblPollMs;
        private System.Windows.Forms.NumericUpDown numPollMs;
        private System.Windows.Forms.Label lblMonLink;
        private System.Windows.Forms.Label lblMonStatus;
        private System.Windows.Forms.Label lblBrandNote;
        private System.Windows.Forms.TabPage tabCollect;
        private System.Windows.Forms.DataGridView dgvCollect;
        private System.Windows.Forms.TextBox txtCollectLog;
        private System.Windows.Forms.Panel panelCollect;
        private System.Windows.Forms.Label lblStoreSlave;
        private System.Windows.Forms.NumericUpDown numStoreSlave;
        private System.Windows.Forms.Label lblStoreStart;
        private System.Windows.Forms.NumericUpDown numStoreStart;
        private System.Windows.Forms.Label lblStoreCount;
        private System.Windows.Forms.NumericUpDown numStoreCount;
        private System.Windows.Forms.Label lblStoreMode;
        private System.Windows.Forms.ComboBox cboStoreMode;
        private System.Windows.Forms.Label lblCollectLink;
        private System.Windows.Forms.Button btnCollectStart;
        private System.Windows.Forms.Button btnCollectStop;
        private System.Windows.Forms.Label lblCollectStatus;
        private System.Windows.Forms.TabPage tabDiag;
        private System.Windows.Forms.DataGridView dgvDiag;
        private System.Windows.Forms.Panel panelDiag;
        private System.Windows.Forms.Button btnDiagClear;
        private System.Windows.Forms.Button btnDiagExport;
        private System.Windows.Forms.Label lblDiagCount;
    }
}
