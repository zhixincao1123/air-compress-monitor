namespace AirCompressMonitor.Comm.Models
{
    /// <summary>
    /// 寄存器字节序。
    /// 不同品牌 PLC 对同一个 32 位量（如压力）的高低字/高低字节排列不一样，
    /// 这就是「多品牌 PLC 适配」里最常踩的坑，所以要显式建模而不是写死。
    /// </summary>
    public enum EndianMode
    {
        /// <summary>由 <see cref="Modbus.EndianDetector"/> 依据量程合理性自动判定。</summary>
        Auto = 0,

        /// <summary>高字在前，字内高字节在前（Modbus 默认，绝大多数设备）。</summary>
        BigEndian = 1,

        /// <summary>低字在前，字内低字节在前。</summary>
        LittleEndian = 2,

        /// <summary>高字在前，但每个字内高低字节互换（部分台达/汇川机型）。</summary>
        BigEndianByteSwap = 3,

        /// <summary>低字在前，且每个字内高低字节互换。</summary>
        LittleEndianByteSwap = 4
    }
}
