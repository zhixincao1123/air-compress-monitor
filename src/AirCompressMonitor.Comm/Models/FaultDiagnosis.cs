using System;

namespace AirCompressMonitor.Comm.Models
{
    /// <summary>故障归类。用于界面分组与统计。</summary>
    public enum FaultCategory
    {
        /// <summary>链路层：连不上、超时、断开。</summary>
        Communication = 0,

        /// <summary>设备层：从站回话但报错，或状态位报警。</summary>
        Device = 1,

        /// <summary>配置层：地址越界、串口参数不对、量程填错。</summary>
        Configuration = 2,

        /// <summary>存储层：库文件被占、磁盘满、权限不足。</summary>
        Storage = 3,

        /// <summary>未归类。</summary>
        Unknown = 4
    }

    /// <summary>
    /// 一条故障诊断结论。刻意做成「现象 / 原因 / 处置建议」三段式 ——
    /// 现场值班的人要的是下一步做什么，不是异常堆栈。
    /// </summary>
    public class FaultDiagnosis
    {
        public DateTime Time { get; set; }

        /// <summary>从站号。0 表示链路级，不针对某一台设备。</summary>
        public byte SlaveId { get; set; }

        public string SlaveName { get; set; }

        public FaultCategory Category { get; set; }

        /// <summary>现象：发生了什么。</summary>
        public string Symptom { get; set; }

        /// <summary>原因：最可能是什么引起的。</summary>
        public string Cause { get; set; }

        /// <summary>处置建议：下一步该做什么。</summary>
        public string Suggestion { get; set; }

        /// <summary>原始错误文本，便于回溯到具体异常。</summary>
        public string RawError { get; set; }

        public AlarmLevel Level { get; set; }

        public FaultDiagnosis()
        {
            Time = DateTime.Now;
            Category = FaultCategory.Unknown;
            Level = AlarmLevel.Warning;
        }

        public override string ToString()
        {
            return string.Format("[{0:HH:mm:ss}] {1} / {2}：{3} → {4}",
                Time, Category, Symptom, Cause, Suggestion);
        }
    }
}
