using System;
using Modbus.Device;

namespace AirCompressMonitor.Comm.Modbus
{
    /// <summary>链路种类。</summary>
    public enum LinkKind
    {
        /// <summary>RS485 / RS232 串口，跑 Modbus RTU。</summary>
        SerialRtu = 0,

        /// <summary>以太网，跑 Modbus TCP。</summary>
        Tcp = 1
    }

    /// <summary>
    /// 物理链路抽象。RTU 和 TCP 的差别全部收敛在这一层，
    /// 上层的轮询、心跳、重连、解析对二者一视同仁 —— 这是「双协议」不写成两套代码的关键。
    /// </summary>
    public interface IModbusLink : IDisposable
    {
        /// <summary>链路种类。</summary>
        LinkKind Kind { get; }

        /// <summary>可读的链路标识，如 "COM3@9600,8,N,1" 或 "192.168.1.10:502"。</summary>
        string Name { get; }

        /// <summary>链路是否已打开。</summary>
        bool IsOpen { get; }

        /// <summary>打开链路。失败抛异常，由上层决定重连策略。</summary>
        void Open();

        /// <summary>关闭链路。要求幂等：重复调用不抛异常。</summary>
        void Close();

        /// <summary>
        /// 基于当前已打开的链路创建 NModbus 主站。
        /// 由调用方负责 Dispose 返回的实例。
        /// </summary>
        IModbusMaster CreateMaster();

        /// <summary>读超时（毫秒）。</summary>
        int ReadTimeoutMs { get; set; }

        /// <summary>写超时（毫秒）。</summary>
        int WriteTimeoutMs { get; set; }

        /// <summary>失败重试次数。</summary>
        int Retries { get; set; }
    }
}
