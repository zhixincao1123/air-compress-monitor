using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Modbus;
using Modbus.Device;
using AirCompressMonitor.Comm.Modbus;

namespace AirCompressMonitor.Tools
{
    /// <summary>探测一个地址的结果。</summary>
    public class ScanResult
    {
        public byte SlaveId { get; set; }

        /// <summary>该地址上是否有设备应答。</summary>
        public bool Alive { get; set; }

        /// <summary>应答耗时（毫秒）。现场排查时，耗差异常往往先于故障出现。</summary>
        public double ElapsedMs { get; set; }

        /// <summary>设备应答了但报异常码（此时它确实在线）。</summary>
        public bool RespondedWithException { get; set; }

        /// <summary>异常码或失败原因。</summary>
        public string Note { get; set; }

        public override string ToString()
        {
            return string.Format("从站 {0}：{1}", SlaveId, Alive ? "在线" : "无应答");
        }
    }

    /// <summary>读一段寄存器的结果。</summary>
    public class RegisterBlock
    {
        public ushort StartAddress { get; set; }
        public ushort[] Values { get; set; }
        public double ElapsedMs { get; set; }

        /// <summary>失败原因，已翻成现场看得懂的话。</summary>
        public string Error { get; set; }

        /// <summary>
        /// 原始异常。诊断层是按异常类型归类的，只留一个文本串就归不了类，
        /// 所以失败时把异常本身也带出来。
        /// </summary>
        public Exception Exception { get; set; }

        public bool Ok { get { return Error == null; } }
    }

    /// <summary>
    /// 一条保持打开的 Modbus 会话。
    ///
    /// 为什么要单独抽这一层：串口是独占资源，开一次的成本也不低
    /// （部分 USB 转串口驱动要上百毫秒）。监视页按秒级轮询，
    /// 每读一次都「开-读-关」的话，一半时间花在开关串口上，
    /// 而且会和别的程序抢口。所以打开一次、反复读、显式关闭。
    /// </summary>
    public class ModbusSession : IDisposable
    {
        private readonly IModbusLink _link;
        private IModbusMaster _master;
        private bool _disposed;

        public ModbusSession(IModbusLink link)
        {
            if (link == null) throw new ArgumentNullException("link");
            _link = link;
        }

        /// <summary>链路标识，如 "COM3@9600,8,N,1"。</summary>
        public string LinkName { get { return _link.Name; } }

        public bool IsOpen { get { return _master != null && _link.IsOpen; } }

        /// <summary>底层主站。扫描要按功能码逐个探测，需要直接拿到它。</summary>
        public IModbusMaster Master { get { return _master; } }

        /// <summary>
        /// 打开链路并建立主站。幂等：已打开时直接返回。
        ///
        /// 超时要在打开之前设：SerialPort 是在 Open 那一刻把超时读进驱动里的，
        /// 打开之后再改，第一次读还是用旧值。
        /// </summary>
        public void Open()
        {
            if (_disposed) throw new ObjectDisposedException("ModbusSession");
            if (IsOpen) return;

            _link.Open();
            try
            {
                _master = _link.CreateMaster();
            }
            catch
            {
                // 主站建不起来就别把串口占着，否则后面每次重试都会报「串口被占用」
                try { _link.Close(); } catch { }
                throw;
            }
        }

        /// <summary>设置读写超时与重试次数。</summary>
        public void SetTimeouts(int readMs, int writeMs, int retries)
        {
            _link.ReadTimeoutMs = readMs;
            _link.WriteTimeoutMs = writeMs;
            _link.Retries = retries;

            var master = _master;
            if (master == null) return;
            try
            {
                master.Transport.ReadTimeout = readMs;
                master.Transport.WriteTimeout = writeMs;
                master.Transport.Retries = retries;
            }
            catch (Exception)
            {
                // 部分传输实现不允许改这些值；改不了就按链路自己的超时走，不算失败
            }
        }

        private IModbusMaster Require()
        {
            var master = _master;
            if (master == null) throw new InvalidOperationException("链路尚未打开");
            return master;
        }

        /// <summary>读保持寄存器。失败抛异常，由调用方决定怎么呈现。</summary>
        public ushort[] ReadHoldingRaw(byte slaveId, ushort start, ushort count)
        {
            return Require().ReadHoldingRegisters(slaveId, start, count);
        }

        /// <summary>读输入寄存器。有些仪表的数据只在输入寄存器里。</summary>
        public ushort[] ReadInputRaw(byte slaveId, ushort start, ushort count)
        {
            return Require().ReadInputRegisters(slaveId, start, count);
        }

        public ushort[] ReadRaw(bool input, byte slaveId, ushort start, ushort count)
        {
            return input
                ? ReadInputRaw(slaveId, start, count)
                : ReadHoldingRaw(slaveId, start, count);
        }

        public void WriteSingleRegister(byte slaveId, ushort address, ushort value)
        {
            Require().WriteSingleRegister(slaveId, address, value);
        }

        /// <summary>
        /// 读一段并保证不抛异常 —— 界面上的「读一次」按钮不该因为设备没应答就弹框。
        /// 失败原因同时以文本和异常两种形式带出。
        /// </summary>
        public RegisterBlock TryRead(byte slaveId, ushort start, ushort count, bool input)
        {
            var block = new RegisterBlock { StartAddress = start };
            var sw = Stopwatch.StartNew();
            try
            {
                Open();
                block.Values = ReadRaw(input, slaveId, start, count);
            }
            catch (Exception ex)
            {
                block.Error = SlaveScanner.Describe(ex);
                block.Exception = ex;
            }
            finally
            {
                sw.Stop();
                block.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            }
            return block;
        }

        /// <summary>关闭会话。幂等。</summary>
        public void Close()
        {
            var master = _master;
            _master = null;
            if (master != null)
            {
                try { master.Dispose(); } catch { }
            }
            try { _link.Close(); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
            try { _link.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// 从站扫描 / 寄存器巡检。
    ///
    /// 现场最常干的两件事：一是「这条 485 线上到底挂了哪几台、地址是多少」，
    /// 二是「这个地址里的数到底是多少」。这两件事不该逼人去开组态软件，
    /// 所以工具集把它们做成两个按钮。
    /// </summary>
    public class SlaveScanner
    {
        /// <summary>单次探测的超时。扫描要快，超时给长了整段扫下来太久。</summary>
        public int ProbeTimeoutMs { get; set; }

        /// <summary>探测失败重试次数。扫描阶段 0 次就够，避免把「没设备」拖成很慢。</summary>
        public int ProbeRetries { get; set; }

        public SlaveScanner()
        {
            ProbeTimeoutMs = 300;
            ProbeRetries = 0;
        }

        /// <summary>
        /// 扫描 [from, to] 区间内的从站。
        ///
        /// 判定「在线」的依据不只是「读到了数据」：设备回一个 Modbus 异常帧
        /// （比如不支持该功能码）也说明它就在线上。把这种情形算成「不在线」
        /// 是很多简易扫描工具的误判来源，会让现场白折腾半天。
        ///
        /// 会话由调用方负责关闭：扫描结束后还要接着读寄存器是常事，
        /// 这里扫完就关会把调用方的会话一起关掉。
        /// </summary>
        public List<ScanResult> Scan(ModbusSession session, byte from, byte to,
                                     Func<IModbusMaster, byte, ushort[]> probe,
                                     Action<ScanResult> onEach,
                                     CancellationToken token)
        {
            if (session == null) throw new ArgumentNullException("session");
            if (probe == null) throw new ArgumentNullException("probe");
            if (to < from) { var t = from; from = to; to = t; }

            var results = new List<ScanResult>();

            try
            {
                session.SetTimeouts(ProbeTimeoutMs, ProbeTimeoutMs, ProbeRetries);
                session.Open();
            }
            catch (Exception ex)
            {
                // 链路都打不开，就没必要逐个地址试了
                var fail = new ScanResult { SlaveId = from, Alive = false, Note = "链路打开失败：" + Describe(ex) };
                results.Add(fail);
                if (onEach != null) onEach(fail);
                return results;
            }

            var master = session.Master;

            for (int id = from; id <= to; id++)
            {
                if (token.IsCancellationRequested) break;

                var result = new ScanResult { SlaveId = (byte)id };
                var sw = Stopwatch.StartNew();
                try
                {
                    var values = probe(master, (byte)id);
                    sw.Stop();
                    result.Alive = true;
                    result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                    result.Note = values != null && values.Length > 0
                        ? string.Format("首寄存器 0x{0:X4}", values[0])
                        : "已应答";
                }
                catch (SlaveException ex)
                {
                    // 设备回了异常帧 —— 它在线上，只是这个功能码/地址它不认
                    sw.Stop();
                    result.Alive = true;
                    result.RespondedWithException = true;
                    result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                    result.Note = string.Format("应答异常码 0x{0:X2}（设备在线，但拒绝该功能码或地址）",
                        (byte)ex.SlaveExceptionCode);
                }
                catch (TimeoutException)
                {
                    sw.Stop();
                    result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                    result.Note = "超时无应答";
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                    result.Note = Describe(ex);
                }

                results.Add(result);
                if (onEach != null) onEach(result);
            }

            return results;
        }

        /// <summary>把异常翻成一句现场看得懂的话。</summary>
        public static string Describe(Exception ex)
        {
            if (ex == null) return "未知错误";

            var slave = ex as SlaveException;
            if (slave != null)
            {
                return string.Format("设备返回异常码 0x{0:X2}（功能码 0x{1:X2}）",
                    (byte)slave.SlaveExceptionCode, (byte)slave.FunctionCode);
            }
            if (ex is TimeoutException) return "超时无应答（地址或波特率可能不对）";
            if (ex is UnauthorizedAccessException) return "串口被占用（可能有别的程序开着同一个口）";
            if (ex is System.IO.IOException) return "串口 IO 失败：" + ex.Message;
            return ex.Message;
        }
    }
}
