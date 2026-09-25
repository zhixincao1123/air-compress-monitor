using System;
using System.Collections.Generic;
using AirCompressMonitor.Comm.Models;

namespace AirCompressMonitor.Comm.Profiles
{
    /// <summary>
    /// 一个 PLC 品牌的寄存器约定模板。
    ///
    /// 为什么要有这一层：不同品牌的 PLC 对「压力放在哪个寄存器、32 位量怎么排字节」
    /// 各有各的约定，这是接现场设备时最费时间的一段。把它固化成模板之后，
    /// 换品牌是「选一个模板」而不是「改一遍代码」。
    ///
    /// 注意：模板里的地址与系数是现场常见约定的整理，**不替代设备手册**。
    /// 每台设备的实际映射以手册为准，模板的作用是给一个正确的起点。
    /// </summary>
    public class PlcBrandProfile
    {
        /// <summary>品牌，如「台达」。</summary>
        public string Brand { get; set; }

        /// <summary>系列，如「DVP 系列」。</summary>
        public string Series { get; set; }

        /// <summary>该模板的适用说明与注意事项。</summary>
        public string Note { get; set; }

        /// <summary>该品牌默认的寄存器字节序。选 Auto 则交给自适应判定。</summary>
        public EndianMode Endian { get; set; }

        /// <summary>保持寄存器起始地址。</summary>
        public ushort StartAddress { get; set; }

        /// <summary>一次读取的寄存器个数。</summary>
        public ushort RegisterCount { get; set; }

        /// <summary>压力寄存器换算系数（原始值 × 系数 = 工程值）。</summary>
        public double PressureScale { get; set; }

        /// <summary>流量/电流寄存器换算系数。</summary>
        public double FlowScale { get; set; }

        /// <summary>压力合理量程，供字节序自适应打分。</summary>
        public double PressureMin { get; set; }
        public double PressureMax { get; set; }

        /// <summary>逐寄存器的含义说明，界面上直接展示，省得对着手册数偏移。</summary>
        public string[] RegisterNotes { get; set; }

        /// <summary>按本模板生成一份从站配置。</summary>
        public SlaveConfig ToSlaveConfig(byte slaveId, string name)
        {
            return new SlaveConfig
            {
                SlaveId = slaveId,
                Name = string.IsNullOrWhiteSpace(name) ? (Brand + " 设备") : name,
                StartAddress = StartAddress,
                RegisterCount = RegisterCount,
                PollIntervalMs = 1000,
                Endian = Endian,
                PressureScale = PressureScale,
                FlowScale = FlowScale,
                PressureMin = PressureMin,
                PressureMax = PressureMax,
                Enabled = true
            };
        }

        public override string ToString()
        {
            return string.Format("{0} {1}", Brand, Series);
        }
    }

    /// <summary>
    /// 内置品牌模板表。新增品牌只需在这里加一条，两个界面自动可见。
    /// </summary>
    public static class PlcBrandCatalog
    {
        private static readonly List<PlcBrandProfile> _all = Build();

        public static IList<PlcBrandProfile> All { get { return _all; } }

        public static PlcBrandProfile Find(string brand)
        {
            if (string.IsNullOrEmpty(brand)) return _all[0];
            foreach (var p in _all)
            {
                if (string.Equals(p.Brand, brand, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return _all[0];
        }

        /// <summary>通用空压机约定：压力/流量/运行时间/状态字/控制字，共 5 个寄存器。</summary>
        private static string[] StandardNotes()
        {
            return new[]
            {
                "压力（原始值 × 系数）",
                "流量 / 电流（原始值 × 系数）",
                "累计运行时间（小时）",
                "状态字（bit0 加载 / bit1 运行 / bit2 排气温度 / bit3 相序 …）",
                "控制字（bit0 启停 / bit2 加载 / bit3 卸载）"
            };
        }

        private static List<PlcBrandProfile> Build()
        {
            var list = new List<PlcBrandProfile>();

            list.Add(new PlcBrandProfile
            {
                Brand = "通用",
                Series = "标准 Modbus 约定",
                Note = "绝大多数支持标准 Modbus 的空压机控制器按这套约定。不确定品牌时先用它。",
                Endian = EndianMode.Auto,
                StartAddress = 0,
                RegisterCount = 5,
                PressureScale = 1.0,
                FlowScale = 0.1,
                PressureMin = 0,
                PressureMax = 400,
                RegisterNotes = StandardNotes()
            });

            list.Add(new PlcBrandProfile
            {
                Brand = "台达",
                Series = "DVP / AS 系列",
                Note = "DVP 系列的保持寄存器常从 0x1000 起编址；部分机型 32 位量字内字节互换。",
                Endian = EndianMode.BigEndianByteSwap,
                StartAddress = 0x1000,
                RegisterCount = 5,
                PressureScale = 0.1,
                FlowScale = 0.01,
                PressureMin = 0,
                PressureMax = 400,
                RegisterNotes = StandardNotes()
            });

            list.Add(new PlcBrandProfile
            {
                Brand = "汇川",
                Series = "H5U / AM 系列",
                Note = "汇川的模拟量常按 0.01 标定；字节序与标准 Modbus 一致。",
                Endian = EndianMode.BigEndian,
                StartAddress = 0,
                RegisterCount = 5,
                PressureScale = 0.01,
                FlowScale = 0.01,
                PressureMin = 0,
                PressureMax = 400,
                RegisterNotes = StandardNotes()
            });

            list.Add(new PlcBrandProfile
            {
                Brand = "西门子",
                Series = "S7-200 SMART / S7-1200（Modbus 从站）",
                Note = "西门子作 Modbus 从站时，保持寄存器 40001 对应地址 0；字序为标准大端。",
                Endian = EndianMode.BigEndian,
                StartAddress = 0,
                RegisterCount = 5,
                PressureScale = 0.1,
                FlowScale = 0.1,
                PressureMin = 0,
                PressureMax = 400,
                RegisterNotes = StandardNotes()
            });

            list.Add(new PlcBrandProfile
            {
                Brand = "三菱",
                Series = "FX3U / FX5U（Modbus 从站）",
                Note = "三菱的 32 位量常为低字在前，且字内字节互换，接错会得到离谱的数值。",
                Endian = EndianMode.LittleEndianByteSwap,
                StartAddress = 0,
                RegisterCount = 5,
                PressureScale = 0.1,
                FlowScale = 0.1,
                PressureMin = 0,
                PressureMax = 400,
                RegisterNotes = StandardNotes()
            });

            list.Add(new PlcBrandProfile
            {
                Brand = "施耐德",
                Series = "M241 / M221",
                Note = "施耐德 %MW 从 0 编址，标准大端。",
                Endian = EndianMode.BigEndian,
                StartAddress = 0,
                RegisterCount = 5,
                PressureScale = 0.1,
                FlowScale = 0.1,
                PressureMin = 0,
                PressureMax = 400,
                RegisterNotes = StandardNotes()
            });

            list.Add(new PlcBrandProfile
            {
                Brand = "信捷",
                Series = "XC / XD 系列",
                Note = "信捷 XD 系列的寄存器地址按 0x0000 起编址，字节序标准。",
                Endian = EndianMode.BigEndian,
                StartAddress = 0,
                RegisterCount = 5,
                PressureScale = 0.1,
                FlowScale = 0.1,
                PressureMin = 0,
                PressureMax = 400,
                RegisterNotes = StandardNotes()
            });

            list.Add(new PlcBrandProfile
            {
                Brand = "英威腾",
                Series = "IVC1 / IVC2 系列",
                Note = "英威腾控制器的模拟量标定常见 0.01；若读数差 10 倍，先查 PressureScale。",
                Endian = EndianMode.BigEndian,
                StartAddress = 0,
                RegisterCount = 5,
                PressureScale = 0.01,
                FlowScale = 0.01,
                PressureMin = 0,
                PressureMax = 400,
                RegisterNotes = StandardNotes()
            });

            return list;
        }
    }
}
