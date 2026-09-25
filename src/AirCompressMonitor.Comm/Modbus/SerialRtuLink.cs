using System;
using System.IO.Ports;
using Modbus.Device;

namespace AirCompressMonitor.Comm.Modbus
{
    /// <summary>
    /// RS485 / RS232 串口链路，跑 Modbus RTU。
    /// </summary>
    public class SerialRtuLink : IModbusLink
    {
        private readonly string _portName;
        private readonly int _baudRate;
        private readonly Parity _parity;
        private readonly int _dataBits;
        private readonly StopBits _stopBits;

        private SerialPort _port;

        public SerialRtuLink(string portName, int baudRate = 9600,
                             Parity parity = Parity.None, int dataBits = 8,
                             StopBits stopBits = StopBits.One)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("串口名不能为空", "portName");

            _portName = portName;
            _baudRate = baudRate;
            _parity = parity;
            _dataBits = dataBits;
            _stopBits = stopBits;
        }

        public LinkKind Kind { get { return LinkKind.SerialRtu; } }

        public string Name
        {
            get
            {
                return string.Format("{0}@{1},{2},{3},{4}",
                    _portName, _baudRate, _dataBits,
                    ParityLetter(_parity), _stopBits == StopBits.One ? "1" : "2");
            }
        }

        public bool IsOpen
        {
            get { return _port != null && _port.IsOpen; }
        }

        public int ReadTimeoutMs { get; set; }
        public int WriteTimeoutMs { get; set; }
        public int Retries { get; set; }

        public SerialRtuLink()
        {
            ReadTimeoutMs = 1000;
            WriteTimeoutMs = 1000;
            Retries = 1;
        }

        public void Open()
        {
            Close();

            // RS485 半双工：不启用流控，否则握手信号会把收发时序搞乱。
            _port = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits)
            {
                ReadTimeout = ReadTimeoutMs > 0 ? ReadTimeoutMs : 1000,
                WriteTimeout = WriteTimeoutMs > 0 ? WriteTimeoutMs : 1000,
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false
            };
            _port.Open();
        }

        public void Close()
        {
            var p = _port;
            _port = null;
            if (p == null) return;
            try
            {
                if (p.IsOpen) p.Close();
            }
            catch
            {
                // 关闭阶段的异常没有处理价值，且不能让重连流程被它打断
            }
            finally
            {
                try { p.Dispose(); } catch { }
            }
        }

        public IModbusMaster CreateMaster()
        {
            if (!IsOpen)
                throw new InvalidOperationException("串口未打开，无法创建 Modbus RTU 主站");

            var master = ModbusSerialMaster.CreateRtu(_port);
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

        private static string ParityLetter(Parity p)
        {
            switch (p)
            {
                case Parity.None: return "N";
                case Parity.Odd: return "O";
                case Parity.Even: return "E";
                case Parity.Mark: return "M";
                case Parity.Space: return "S";
                default: return "N";
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
