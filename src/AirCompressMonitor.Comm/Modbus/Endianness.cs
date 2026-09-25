using System;
using System.Collections.Generic;
using AirCompressMonitor.Comm.Models;

namespace AirCompressMonitor.Comm.Modbus
{
    /// <summary>
    /// 字节序工具。所有「同一个数，不同品牌 PLC 发出来字节排列不一样」的换算都收敛在这里。
    /// </summary>
    public static class Endianness
    {
        /// <summary>16 位字内高低字节互换：0x0064(100) ↔ 0x6400(25600)。</summary>
        public static ushort SwapBytes(ushort value)
        {
            return (ushort)(((value >> 8) & 0xFF) | ((value & 0xFF) << 8));
        }

        /// <summary>
        /// 把一个 16 位寄存器按指定字节序还原成「人类读法」的值。
        /// </summary>
        public static ushort ApplyWord(ushort raw, EndianMode mode)
        {
            switch (mode)
            {
                case EndianMode.BigEndianByteSwap:
                case EndianMode.LittleEndianByteSwap:
                    return SwapBytes(raw);
                default:
                    // 单个 16 位寄存器时 BigEndian 与 LittleEndian 的「字序」无从体现，
                    // 都按原样返回；真正的差别在 32 位量上，见 ToUInt32。
                    return raw;
            }
        }

        /// <summary>
        /// 把连续两个字按指定字节序拼成 32 位量。
        /// </summary>
        public static uint ToUInt32(ushort[] registers, int index, EndianMode mode)
        {
            if (registers == null) throw new ArgumentNullException("registers");
            if (index < 0 || index + 1 >= registers.Length)
                throw new ArgumentOutOfRangeException("index", "32 位量需要 index 与 index+1 两个寄存器");

            ushort a = registers[index];
            ushort b = registers[index + 1];

            bool lowWordFirst =
                mode == EndianMode.LittleEndian || mode == EndianMode.LittleEndianByteSwap;
            bool byteSwap =
                mode == EndianMode.BigEndianByteSwap || mode == EndianMode.LittleEndianByteSwap;

            if (byteSwap)
            {
                a = SwapBytes(a);
                b = SwapBytes(b);
            }

            ushort high = lowWordFirst ? b : a;
            ushort low = lowWordFirst ? a : b;
            return ((uint)high << 16) | low;
        }

        /// <summary>
        /// 该字节序在 16 位寄存器上等价于哪个规范模式。
        /// 单个寄存器时 BigEndian≡LittleEndian、BigEndianByteSwap≡LittleEndianByteSwap，
        /// 归一化后可以避免把同一个解码结果当成两个候选重复打分。
        /// </summary>
        public static EndianMode CanonicalizeForWord(EndianMode mode)
        {
            switch (mode)
            {
                case EndianMode.LittleEndian:
                    return EndianMode.BigEndian;
                case EndianMode.LittleEndianByteSwap:
                    return EndianMode.BigEndianByteSwap;
                default:
                    return mode;
            }
        }
    }

    /// <summary>
    /// 字节序自适应判定。
    ///
    /// 思路：同一个寄存器值，用错的字节序解出来通常是「离谱但又不报错」的数
    /// （100 变成 25600）。所以拿配置的合理量程当尺子，对候选字节序逐个打分，
    /// 落在量程内比例最高的胜出；样本不足或分不出高下时不下结论，继续观察。
    /// 这样多品牌混接时不必为每台设备手填字节序。
    /// </summary>
    public class EndianDetector
    {
        private readonly double _scale;
        private readonly double _min;
        private readonly double _max;
        private readonly int _window;

        private readonly List<ushort> _samples = new List<ushort>();

        /// <summary>判定所需的领先幅度：胜出者命中率要比次优高这么多才算「分得清」。</summary>
        private const double LockMargin = 0.30;

        public EndianDetector(double scale, double min, double max, int window = 24)
        {
            _scale = scale <= 0 ? 1.0 : scale;
            _min = min;
            _max = max;
            _window = window < 4 ? 4 : window;
        }

        /// <summary>当前判定的字节序。</summary>
        public EndianMode Current { get; private set; }

        /// <summary>是否已能给出可信结论（样本够 + 分得清）。</summary>
        public bool IsLocked { get; private set; }

        /// <summary>喂入一个原始寄存器值。</summary>
        public void Feed(ushort raw)
        {
            _samples.Add(raw);
            if (_samples.Count > _window)
                _samples.RemoveAt(0);

            Evaluate();
        }

        /// <summary>清空样本，设备更换或参数调整后重新判定。</summary>
        public void Reset()
        {
            _samples.Clear();
            IsLocked = false;
            Current = EndianMode.BigEndian;
        }

        /// <summary>
        /// 给出本帧应当采用的字节序：配置里写了具体值就听配置，只有 Auto 才用判定结果。
        /// </summary>
        public EndianMode Resolve(EndianMode configured)
        {
            if (configured != EndianMode.Auto)
                return configured;
            return IsLocked ? Current : EndianMode.BigEndian;
        }

        private void Evaluate()
        {
            if (_samples.Count < _window)
            {
                IsLocked = false;
                return;
            }

            // 只对 16 位寄存器打分，候选只有两种实质不同的解码：原样 与 字内换字节
            int hitsPlain = 0, hitsSwapped = 0;
            bool plainStuck = true, swappedStuck = true;
            double firstPlain = 0, firstSwapped = 0;

            for (int i = 0; i < _samples.Count; i++)
            {
                double plain = _samples[i] * _scale;
                double swapped = Endianness.SwapBytes(_samples[i]) * _scale;

                if (InRange(plain)) hitsPlain++;
                if (InRange(swapped)) hitsSwapped++;

                if (i == 0) { firstPlain = plain; firstSwapped = swapped; }
                else
                {
                    if (Math.Abs(plain - firstPlain) > double.Epsilon) plainStuck = false;
                    if (Math.Abs(swapped - firstSwapped) > double.Epsilon) swappedStuck = false;
                }
            }

            // 全窗口一个值都没变过，说明设备根本没在刷新，此时任何判定都不可信
            if (plainStuck && swappedStuck)
            {
                IsLocked = false;
                return;
            }

            double ratePlain = (double)hitsPlain / _samples.Count;
            double rateSwapped = (double)hitsSwapped / _samples.Count;

            if (Math.Abs(ratePlain - rateSwapped) < LockMargin)
            {
                // 两种解码都说得通或都说不通 —— 不下结论，避免锁死到错的字节序
                IsLocked = false;
                return;
            }

            Current = ratePlain > rateSwapped
                ? EndianMode.BigEndian
                : EndianMode.BigEndianByteSwap;
            IsLocked = true;
        }

        private bool InRange(double value)
        {
            return value >= _min && value <= _max;
        }
    }
}
