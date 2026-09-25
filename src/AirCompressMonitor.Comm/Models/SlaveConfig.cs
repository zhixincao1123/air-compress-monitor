using System;

namespace AirCompressMonitor.Comm.Models
{
    /// <summary>
    /// 一个从站的采集配置。多从站轮询时，每个从站一份。
    /// </summary>
    public class SlaveConfig
    {
        /// <summary>从站地址，合法范围 1-247。</summary>
        public byte SlaveId { get; set; } = 1;

        /// <summary>设备名（如「1#空压机」），落库和报警都用它，避免只看到裸地址。</summary>
        public string Name { get; set; } = "空压机";

        /// <summary>起始寄存器地址。</summary>
        public ushort StartAddress { get; set; } = 0;

        /// <summary>一次读多少个寄存器。默认 5：压力/流量/时间/状态字/控制字。</summary>
        public ushort RegisterCount { get; set; } = 5;

        /// <summary>该从站的轮询间隔（毫秒）。不同从站可以不同，避免慢设备拖垮整条链路。</summary>
        public int PollIntervalMs { get; set; } = 1000;

        /// <summary>字节序；Auto 时由自适应解析决定。</summary>
        public EndianMode Endian { get; set; } = EndianMode.Auto;

        /// <summary>压力寄存器原始值的换算系数（原始值 × Scale = 工程值）。</summary>
        public double PressureScale { get; set; } = 1.0;

        /// <summary>流量寄存器原始值的换算系数。</summary>
        public double FlowScale { get; set; } = 0.1;

        /// <summary>压力合理量程下限，供字节序自适应打分用。</summary>
        public double PressureMin { get; set; } = 0;

        /// <summary>压力合理量程上限，供字节序自适应打分用。</summary>
        public double PressureMax { get; set; } = 400;

        /// <summary>是否参与轮询。临时检修的设备可以置 false 而不必从列表里删掉。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>连续失败多少次判定该从站掉线。</summary>
        public int FailureThreshold { get; set; } = 3;

        public SlaveConfig Clone()
        {
            return (SlaveConfig)MemberwiseClone();
        }

        public override string ToString()
        {
            return string.Format("{0}({1})", Name, SlaveId);
        }
    }
}
