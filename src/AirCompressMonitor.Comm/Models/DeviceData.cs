using System;

namespace AirCompressMonitor.Comm.Models
{
    /// <summary>
    /// 一次采集得到的一帧设备数据。
    /// 由 <see cref="Modbus.ModbusMonitorClient"/> 在从站轮询命中后构造并分发。
    /// </summary>
    public class DeviceData
    {
        /// <summary>来源从站地址（1-247）。多从站轮询时用于区分是哪台设备。</summary>
        public byte SlaveId { get; set; }

        /// <summary>从站名称，来自 <see cref="SlaveConfig.Name"/>，便于日志和入库可读。</summary>
        public string SlaveName { get; set; }

        /// <summary>本帧的采集时刻（本地时间），入库和归档都以此为准。</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>控制字寄存器（地址 4）的原始值，写回时以它为基线做位操作。</summary>
        public ushort Reg4Value { get; set; }

        public bool CtrlB0 { get; set; }
        public bool CtrlB1 { get; set; }
        public bool CtrlB2 { get; set; }
        public bool CtrlB3 { get; set; }

        public bool Bit0 { get; set; }
        public bool Bit1 { get; set; }
        public bool Bit2 { get; set; }
        public bool Bit3 { get; set; }
        public bool Bit4 { get; set; }
        public bool Bit5 { get; set; }
        public bool Bit6 { get; set; }
        public bool Bit7 { get; set; }
        public bool Bit8 { get; set; }

        public double PressureValue { get; set; }
        public double Flow { get; set; }
        public int Time { get; set; }

        /// <summary>
        /// 本帧的原始寄存器（未经字节序还原）。
        /// 出问题时靠它回溯「是设备发的数不对，还是我们解析错了」。
        /// </summary>
        public ushort[] RawRegisters { get; set; }

        /// <summary>本帧解析时实际采用的字节序，随帧落盘，便于事后核对自适应结果。</summary>
        public EndianMode Endian { get; set; }
    }
}
