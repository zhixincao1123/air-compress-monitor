using System;
using System.Net.Sockets;
using Modbus.Device;

namespace AirCompressMonitor.Comm.Modbus
{
    /// <summary>
    /// 以太网链路，跑 Modbus TCP（默认端口 502）。
    /// </summary>
    public class TcpLink : IModbusLink
    {
        private readonly string _host;
        private readonly int _port;
        private readonly int _connectTimeoutMs;

        private TcpClient _client;

        public TcpLink(string host, int port = 502, int connectTimeoutMs = 3000)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("主机地址不能为空", "host");

            _host = host;
            _port = port;
            _connectTimeoutMs = connectTimeoutMs;
            ReadTimeoutMs = 1000;
            WriteTimeoutMs = 1000;
            Retries = 1;
        }

        public LinkKind Kind { get { return LinkKind.Tcp; } }

        public string Name
        {
            get { return string.Format("{0}:{1}", _host, _port); }
        }

        public bool IsOpen
        {
            get { return _client != null && _client.Connected; }
        }

        public int ReadTimeoutMs { get; set; }
        public int WriteTimeoutMs { get; set; }
        public int Retries { get; set; }

        public void Open()
        {
            Close();

            // 显式控制连接超时：TcpClient 默认超时是分钟级，放在重连循环里会卡死整个采集线程。
            var client = new TcpClient();
            var async = client.BeginConnect(_host, _port, null, null);
            if (!async.AsyncWaitHandle.WaitOne(_connectTimeoutMs))
            {
                try { client.Close(); } catch { }
                throw new TimeoutException(
                    string.Format("连接 {0}:{1} 超时（{2}ms）", _host, _port, _connectTimeoutMs));
            }
            client.EndConnect(async);

            client.NoDelay = true;                       // 采集是小包高频，Nagle 会引入额外延迟
            client.ReceiveTimeout = ReadTimeoutMs > 0 ? ReadTimeoutMs : 1000;
            client.SendTimeout = WriteTimeoutMs > 0 ? WriteTimeoutMs : 1000;

            _client = client;
        }

        public void Close()
        {
            var c = _client;
            _client = null;
            if (c == null) return;
            try { c.Close(); }
            catch { }
            finally
            {
                try { c.Dispose(); } catch { }
            }
        }

        public IModbusMaster CreateMaster()
        {
            if (!IsOpen)
                throw new InvalidOperationException("TCP 链路未打开，无法创建 Modbus TCP 主站");

            var master = ModbusIpMaster.CreateIp(_client);
            master.Transport.ReadTimeout = ReadTimeoutMs > 0 ? ReadTimeoutMs : 1000;
            master.Transport.WriteTimeout = WriteTimeoutMs > 0 ? WriteTimeoutMs : 1000;
            master.Transport.Retries = Retries;
            master.Transport.WaitToRetryMilliseconds = 50;
            return master;
        }

        public void Dispose()
        {
            Close();
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
