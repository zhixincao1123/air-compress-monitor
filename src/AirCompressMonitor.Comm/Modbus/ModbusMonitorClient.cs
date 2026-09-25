using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Modbus.Device;
using AirCompressMonitor.Comm.Models;

namespace AirCompressMonitor.Comm.Modbus
{
    /// <summary>
    /// Modbus 采集主站。RTU / TCP 由注入的 <see cref="IModbusLink"/> 决定，
    /// 本类只关心「怎么轮、怎么判活、断了怎么回来、回来怎么解析」。
    ///
    /// 职责：
    ///   1. 多从站轮询 —— 单条链路按各从站自己的周期轮流发请求，慢设备不拖垮快设备
    ///   2. 心跳保活 —— 独立于业务轮询探链路，识别「连着但已经不通」的假活
    ///   3. 断线自动重连 —— 指数退避重建链路，恢复后自动续采，不需要人工点连接
    ///   4. 大小端自适应 —— 每从站一个判定器，按量程合理性自动选字节序
    /// </summary>
    public class ModbusMonitorClient : IDisposable
    {
        private readonly IModbusLink _link;
        private readonly List<SlaveConfig> _slaves = new List<SlaveConfig>();
        private readonly Dictionary<byte, SlaveRuntime> _runtime = new Dictionary<byte, SlaveRuntime>();
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private readonly object _masterLock = new object();

        private IModbusMaster _master;
        private CancellationTokenSource _cts;
        private Task _pollLoop;
        private Task _heartbeatLoop;

        private int _reconnecting;              // Interlocked 保护，保证同一时刻只有一条重连流程
        private volatile bool _disposed;

        // ---------- 可调参数 ----------
        /// <summary>轮询调度的心跳节拍：每隔这么久检查一次「哪些从站到点了」。</summary>
        public int PollTickMs { get; set; }

        /// <summary>心跳间隔。默认 5 秒，远低于常见设备的 TCP 空闲断链阈值。</summary>
        public int HeartbeatIntervalMs { get; set; }

        /// <summary>连续多少次心跳失败判定链路已死。</summary>
        public int HeartbeatFailureThreshold { get; set; }

        /// <summary>重连首次退避时长。</summary>
        public int ReconnectBaseDelayMs { get; set; }

        /// <summary>重连退避上限，避免无限翻倍。</summary>
        public int ReconnectMaxDelayMs { get; set; }

        // ---------- 对外状态 ----------
        public CommStatistics Statistics { get; private set; }

        /// <summary>链路是否可用（已打开且主站已建立）。</summary>
        public bool IsConnected
        {
            get
            {
                lock (_masterLock)
                {
                    return _master != null && _link.IsOpen;
                }
            }
        }

        /// <summary>链路标识，UI 直接显示。</summary>
        public string LinkName { get { return _link.Name; } }

        public LinkKind LinkKind { get { return _link.Kind; } }

        /// <summary>当前从站配置列表。运行中增删会在下一轮生效。</summary>
        public IList<SlaveConfig> Slaves { get { return _slaves; } }

        // ---------- 事件 ----------
        /// <summary>采集到一帧数据。注意：在后台线程触发，UI 层需自行切线程。</summary>
        public event Action<DeviceData> DataReceived;

        /// <summary>报警或恢复。</summary>
        public event Action<AlarmInfo> AlarmRaised;

        /// <summary>链路通断变化：(是否连通, 说明)。</summary>
        public event Action<bool, string> ConnectionChanged;

        /// <summary>错误信息，用于日志与界面提示。</summary>
        public event Action<string> Error;

        /// <summary>统计快照，按固定节拍推送，避免 UI 被高频刷新拖垮。</summary>
        public event Action<CommStatistics> StatisticsUpdated;

        public ModbusMonitorClient(IModbusLink link)
        {
            if (link == null) throw new ArgumentNullException("link");

            _link = link;
            PollTickMs = 200;
            HeartbeatIntervalMs = 5000;
            HeartbeatFailureThreshold = 3;
            ReconnectBaseDelayMs = 2000;
            ReconnectMaxDelayMs = 60000;
            Statistics = new CommStatistics();
        }

        // =====================================================================
        // 生命周期
        // =====================================================================

        /// <summary>
        /// 打开链路并开始采集。失败不抛异常：交给重连流程去恢复，
        /// 上层界面保持「正在重连」而不是直接崩掉。
        /// </summary>
        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException("ModbusMonitorClient");
            if (_pollLoop != null) return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                OpenLinkAndMaster();
                RaiseConnectionChanged(true, string.Format("已连接 {0}", _link.Name));
            }
            catch (Exception ex)
            {
                RaiseError(string.Format("首次连接 {0} 失败：{1}", _link.Name, ex.Message));
                RaiseConnectionChanged(false, string.Format("连接失败：{0}", ex.Message));
                // 交给后台重连，不阻塞调用方
                BeginReconnect("首次连接失败");
            }

            _pollLoop = Task.Run(() => PollLoopAsync(token));
            _heartbeatLoop = Task.Run(() => HeartbeatLoopAsync(token));
        }

        /// <summary>停止采集并释放链路。</summary>
        public void Stop()
        {
            var cts = _cts;
            _cts = null;

            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
            }

            var loops = new[] { _pollLoop, _heartbeatLoop };
            _pollLoop = null;
            _heartbeatLoop = null;

            foreach (var t in loops)
            {
                if (t == null) continue;
                try { t.Wait(TimeSpan.FromSeconds(3)); }
                catch { /* 取消引发的异常属于预期 */ }
            }

            if (cts != null)
            {
                try { cts.Dispose(); } catch { }
            }

            CloseLinkAndMaster();
            RaiseConnectionChanged(false, "已断开");
        }

        private void OpenLinkAndMaster()
        {
            _link.Open();

            IModbusMaster master;
            try
            {
                master = _link.CreateMaster();
            }
            catch
            {
                _link.Close();
                throw;
            }

            lock (_masterLock)
            {
                var old = _master;
                _master = master;
                if (old != null)
                {
                    try { old.Dispose(); } catch { }
                }
            }

            // 链路重建后，各从站的连续失败计数清零，否则旧的失败会立刻再触发一次重连
            lock (_runtime)
            {
                foreach (var rt in _runtime.Values)
                {
                    rt.ConsecutiveFailures = 0;
                    rt.IsOnline = false;
                }
            }
        }

        private void CloseLinkAndMaster()
        {
            lock (_masterLock)
            {
                if (_master != null)
                {
                    try { _master.Dispose(); } catch { }
                    _master = null;
                }
            }
            try { _link.Close(); } catch { }
        }

        // =====================================================================
        // 轮询
        // =====================================================================

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 重连期间不发业务请求，避免和重建流程抢链路
                    if (IsConnected && Volatile.Read(ref _reconnecting) == 0)
                    {
                        var now = DateTime.Now;
                        foreach (var slave in SnapshotSlaves())
                        {
                            if (token.IsCancellationRequested) break;
                            if (!slave.Enabled) continue;

                            var rt = GetRuntime(slave.SlaveId);
                            if ((now - rt.LastPollTime).TotalMilliseconds < slave.PollIntervalMs)
                                continue;

                            rt.LastPollTime = now;
                            await PollSlaveAsync(slave, token).ConfigureAwait(false);
                        }

                        RaiseStatistics();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    RaiseError(string.Format("轮询循环异常：{0}", ex.Message));
                }

                try
                {
                    await Task.Delay(PollTickMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task PollSlaveAsync(SlaveConfig slave, CancellationToken token)
        {
            await _ioLock.WaitAsync(token).ConfigureAwait(false);
            var sw = Stopwatch.StartNew();
            try
            {
                var master = GetMaster();
                if (master == null) return;

                ushort[] regs = await Task.Run(() =>
                    master.ReadHoldingRegisters(slave.SlaveId, slave.StartAddress, slave.RegisterCount),
                    token).ConfigureAwait(false);

                sw.Stop();
                Statistics.TotalRequests++;
                Statistics.LastLatencyMs = sw.Elapsed.TotalMilliseconds;

                var data = BuildDeviceData(slave, regs);
                OnSlaveSucceeded(slave);
                RaiseDataReceived(data);
            }
            catch (OperationCanceledException)
            {
                // 正常停机
            }
            catch (Exception ex)
            {
                sw.Stop();
                Statistics.TotalRequests++;
                Statistics.FailedRequests++;
                OnSlaveFailed(slave, ex);
            }
            finally
            {
                try { _ioLock.Release(); } catch { }
            }
        }

        // =====================================================================
        // 心跳保活
        // =====================================================================

        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(HeartbeatIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (Volatile.Read(ref _reconnecting) != 0) continue;

                await HeartbeatOnceAsync(token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 探一次链路。取第一个启用从站读 1 个寄存器 —— 只关心「有没有回应」，
        /// 不关心值是多少，所以代价极低，可以放心按秒级频率跑。
        /// </summary>
        private async Task HeartbeatOnceAsync(CancellationToken token)
        {
            if (!IsConnected) return;

            var probe = FirstEnabledSlave();
            if (probe == null) return;

            await _ioLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var master = GetMaster();
                if (master == null) return;

                await Task.Run(() =>
                    master.ReadHoldingRegisters(probe.SlaveId, probe.StartAddress, 1),
                    token).ConfigureAwait(false);

                Statistics.HeartbeatOk++;
                Statistics.Timestamp = DateTime.Now;
                _heartbeatFailures = 0;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Statistics.HeartbeatFail++;
                _heartbeatFailures++;
                RaiseError(string.Format("心跳失败({0}/{1})：{2}",
                    _heartbeatFailures, HeartbeatFailureThreshold, ex.Message));

                if (_heartbeatFailures >= HeartbeatFailureThreshold)
                {
                    _heartbeatFailures = 0;
                    // 心跳连续失败 = 链路假活，主动重建，不等业务请求超时才发现
                    BeginReconnect("心跳连续失败");
                }
            }
            finally
            {
                try { _ioLock.Release(); } catch { }
            }
        }

        private int _heartbeatFailures;

        // =====================================================================
        // 断线自动重连
        // =====================================================================

        /// <summary>
        /// 发起重连。用 Interlocked 保证并发调用（心跳失败 + 业务失败同时发生）只跑一条重连流程。
        /// </summary>
        private void BeginReconnect(string reason)
        {
            if (_disposed || _cts == null) return;
            if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0) return;

            var token = _cts.Token;
            Task.Run(() => ReconnectLoopAsync(reason, token));
        }

        private async Task ReconnectLoopAsync(string reason, CancellationToken token)
        {
            try
            {
                RaiseConnectionChanged(false, string.Format("连接中断（{0}），正在自动重连…", reason));

                int delay = ReconnectBaseDelayMs;
                int attempt = 0;

                while (!token.IsCancellationRequested && !_disposed)
                {
                    attempt++;
                    try
                    {
                        await Task.Delay(delay, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    try
                    {
                        CloseLinkAndMaster();
                        OpenLinkAndMaster();

                        Statistics.ReconnectCount++;
                        Statistics.Timestamp = DateTime.Now;
                        RaiseConnectionChanged(true,
                            string.Format("重连成功（第 {0} 次尝试），已恢复 {1}", attempt, _link.Name));
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        RaiseError(string.Format("第 {0} 次重连失败：{1}（{2}ms 后重试）",
                            attempt, ex.Message, delay));

                        // 指数退避并封顶：链路长时间不可用时不能把 CPU 和日志打满
                        delay = Math.Min(delay * 2, ReconnectMaxDelayMs);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _reconnecting, 0);
            }
        }

        // =====================================================================
        // 解析
        // =====================================================================

        private DeviceData BuildDeviceData(SlaveConfig slave, ushort[] regs)
        {
            if (regs == null || regs.Length < 4)
                throw new InvalidOperationException(
                    string.Format("从站 {0} 返回寄存器数量不足（{1} < 4）",
                        slave.SlaveId, regs == null ? 0 : regs.Length));

            var rt = GetRuntime(slave.SlaveId);

            // 压力寄存器是 16 位，用它喂判定器；判定锁定前先按大端处理
            rt.EndianDetector.Feed(regs[0]);
            var mode = rt.EndianDetector.Resolve(slave.Endian);
            ushort pressureRaw = Endianness.ApplyWord(regs[0], mode);

            ushort statusWord = regs[3];
            ushort ctrlWord = regs.Length > 4 ? regs[4] : (ushort)0;

            var data = new DeviceData
            {
                SlaveId = slave.SlaveId,
                SlaveName = slave.Name,
                Timestamp = DateTime.Now,
                Endian = mode,
                RawRegisters = regs,

                PressureValue = pressureRaw * slave.PressureScale,
                Flow = regs[1] * slave.FlowScale,
                Time = regs[2],

                Bit0 = (statusWord & (1 << 0)) != 0,
                Bit1 = (statusWord & (1 << 1)) != 0,
                Bit2 = (statusWord & (1 << 2)) != 0,
                Bit3 = (statusWord & (1 << 3)) != 0,
                Bit4 = (statusWord & (1 << 4)) != 0,
                Bit5 = (statusWord & (1 << 5)) != 0,
                Bit6 = (statusWord & (1 << 6)) != 0,
                Bit7 = (statusWord & (1 << 7)) != 0,

                CtrlB0 = (ctrlWord & (1 << 0)) != 0,
                CtrlB1 = (ctrlWord & (1 << 1)) != 0,
                CtrlB2 = (ctrlWord & (1 << 2)) != 0,
                CtrlB3 = (ctrlWord & (1 << 3)) != 0,
                Reg4Value = ctrlWord
            };

            // 第 9 位状态在原实现里取自状态字，这里保持一致
            data.Bit8 = (statusWord & (1 << 8)) != 0;

            CheckAlarms(slave, data);
            return data;
        }

        /// <summary>位名 → 是否报警。名称沿用现场定义。</summary>
        private static readonly string[][] BitAlarmNames =
        {
            new[] { "Bit0", "加载/卸载" },
            new[] { "Bit1", "运行/停止" },
            new[] { "Bit2", "排气温度过高" },
            new[] { "Bit3", "相序错误" },
            new[] { "Bit4", "主电机电流故障" },
            new[] { "Bit5", "空滤器堵塞" },
            new[] { "Bit6", "风机电流故障" },
            new[] { "Bit7", "供气压力过高" },
            new[] { "Bit8", "油分气堵塞" },
        };

        private void CheckAlarms(SlaveConfig slave, DeviceData data)
        {
            var rt = GetRuntime(slave.SlaveId);
            bool[] current =
            {
                data.Bit0, data.Bit1, data.Bit2, data.Bit3, data.Bit4,
                data.Bit5, data.Bit6, data.Bit7, data.Bit8
            };

            for (int i = 0; i < current.Length; i++)
            {
                bool prev;
                if (!rt.LastBitStates.TryGetValue(i, out prev))
                {
                    // 首帧只记基线，不产生报警 —— 否则每次启动都会刷一屏历史状态
                    rt.LastBitStates[i] = current[i];
                    continue;
                }

                if (prev == current[i]) continue;
                rt.LastBitStates[i] = current[i];

                RaiseAlarm(new AlarmInfo
                {
                    Time = DateTime.Now,
                    SlaveId = slave.SlaveId,
                    SlaveName = slave.Name,
                    State = current[i] ? "报警" : "恢复",
                    Message = string.Format("{0}{1}", BitAlarmNames[i][1], current[i] ? "触发" : "恢复"),
                    Level = current[i] ? AlarmLevel.Fault : AlarmLevel.Info
                });
            }
        }

        // =====================================================================
        // 从站运行时状态
        // =====================================================================

        private class SlaveRuntime
        {
            public DateTime LastPollTime = DateTime.MinValue;
            public int ConsecutiveFailures;
            public bool IsOnline;
            public readonly Dictionary<int, bool> LastBitStates = new Dictionary<int, bool>();
            public EndianDetector EndianDetector;
        }

        private SlaveRuntime GetRuntime(byte slaveId)
        {
            lock (_runtime)
            {
                SlaveRuntime rt;
                if (!_runtime.TryGetValue(slaveId, out rt))
                {
                    rt = new SlaveRuntime();
                    var cfg = FindSlave(slaveId);
                    double scale = cfg != null ? cfg.PressureScale : 1.0;
                    double min = cfg != null ? cfg.PressureMin : 0;
                    double max = cfg != null ? cfg.PressureMax : 400;
                    rt.EndianDetector = new EndianDetector(scale, min, max);
                    _runtime[slaveId] = rt;
                }
                return rt;
            }
        }

        private SlaveConfig FindSlave(byte slaveId)
        {
            foreach (var s in SnapshotSlaves())
            {
                if (s.SlaveId == slaveId) return s;
            }
            return null;
        }

        private List<SlaveConfig> SnapshotSlaves()
        {
            // 运行中允许上层增删从站，这里拷一份再遍历，避免枚举时集合被改
            lock (_slaves)
            {
                return new List<SlaveConfig>(_slaves);
            }
        }

        private SlaveConfig FirstEnabledSlave()
        {
            foreach (var s in SnapshotSlaves())
            {
                if (s.Enabled) return s;
            }
            return null;
        }

        private void OnSlaveSucceeded(SlaveConfig slave)
        {
            var rt = GetRuntime(slave.SlaveId);
            rt.ConsecutiveFailures = 0;
            rt.IsOnline = true;
        }

        private void OnSlaveFailed(SlaveConfig slave, Exception ex)
        {
            var rt = GetRuntime(slave.SlaveId);
            rt.ConsecutiveFailures++;

            RaiseError(string.Format("{0} 采集失败（连续 {1} 次）：{2}",
                slave.Name, rt.ConsecutiveFailures, ex.Message));

            if (rt.IsOnline && rt.ConsecutiveFailures >= slave.FailureThreshold)
            {
                rt.IsOnline = false;
                RaiseAlarm(new AlarmInfo
                {
                    Time = DateTime.Now,
                    SlaveId = slave.SlaveId,
                    SlaveName = slave.Name,
                    State = "报警",
                    Message = string.Format("{0} 通信中断", slave.Name),
                    Level = AlarmLevel.Fault
                });
            }

            // 首个从站都不通，说明问题在链路而非单台设备，直接触发重连
            if (rt.ConsecutiveFailures >= slave.FailureThreshold && IsProbeSlave(slave))
                BeginReconnect(string.Format("{0} 连续无响应", slave.Name));
        }

        private bool IsProbeSlave(SlaveConfig slave)
        {
            var probe = FirstEnabledSlave();
            return probe != null && probe.SlaveId == slave.SlaveId;
        }

        private IModbusMaster GetMaster()
        {
            lock (_masterLock)
            {
                return _master;
            }
        }

        // =====================================================================
        // 远程控制
        // =====================================================================

        /// <summary>写单个保持寄存器。返回是否成功。</summary>
        public bool WriteSingleRegister(byte slaveId, ushort address, ushort value)
        {
            if (!IsConnected) return false;

            _ioLock.Wait();
            try
            {
                var master = GetMaster();
                if (master == null) return false;

                master.WriteSingleRegister(slaveId, address, value);
                Statistics.TotalRequests++;
                return true;
            }
            catch (Exception ex)
            {
                Statistics.TotalRequests++;
                Statistics.FailedRequests++;
                RaiseError(string.Format("写寄存器 {0}[{1}] 失败：{2}", slaveId, address, ex.Message));
                BeginReconnect("写寄存器失败");
                return false;
            }
            finally
            {
                try { _ioLock.Release(); } catch { }
            }
        }

        /// <summary>
        /// 基于当前控制字改某一位后写回。位操作在本地算好再一次性下发，
        /// 避免「读-改-写」三步之间被下一轮轮询插进来。
        /// </summary>
        public bool WriteControlBit(byte slaveId, ushort controlAddress,
                                    ushort currentValue, int bitIndex, bool on)
        {
            ushort value = on
                ? (ushort)(currentValue | (1 << bitIndex))
                : (ushort)(currentValue & ~(1 << bitIndex));
            return WriteSingleRegister(slaveId, controlAddress, value);
        }

        // =====================================================================
        // 事件派发（一律吞掉订阅方的异常，采集链路不能被 UI 异常打断）
        // =====================================================================

        private void RaiseDataReceived(DeviceData data)
        {
            var h = DataReceived;
            if (h == null) return;
            try { h(data); } catch { }
        }

        private void RaiseAlarm(AlarmInfo alarm)
        {
            var h = AlarmRaised;
            if (h == null) return;
            try { h(alarm); } catch { }
        }

        private void RaiseConnectionChanged(bool connected, string message)
        {
            var h = ConnectionChanged;
            if (h == null) return;
            try { h(connected, message); } catch { }
        }

        private void RaiseError(string message)
        {
            var h = Error;
            if (h == null) return;
            try { h(message); } catch { }
        }

        private DateTime _lastStatsPush = DateTime.MinValue;

        private void RaiseStatistics()
        {
            // 统计按 1 秒推一次即可，和轮询节拍解耦，免得 UI 被高频刷新拖住
            var now = DateTime.Now;
            if ((now - _lastStatsPush).TotalMilliseconds < 1000) return;
            _lastStatsPush = now;

            Statistics.Timestamp = now;
            var h = StatisticsUpdated;
            if (h == null) return;
            try { h(Statistics); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { Stop(); } catch { }
            try { _ioLock.Dispose(); } catch { }
            try { _link.Dispose(); } catch { }
        }
    }
}
