using System;
using System.Collections.Generic;
using AirCompressMonitor.Comm.Models;

namespace AirCompressMonitor.Storage
{
    /// <summary>
    /// 存储后端契约。SQLite 与 CSV 都实现它，上层不必知道自己写在哪儿。
    /// </summary>
    public interface IDataStore : IDisposable
    {
        /// <summary>后端名称，用于日志与界面显示。</summary>
        string Name { get; }

        /// <summary>准备存储介质（建库、建目录、开文件）。</summary>
        void Initialize();

        /// <summary>追加一条。允许内部缓冲，不保证立即落盘。</summary>
        void Append(DeviceData data);

        /// <summary>把缓冲全部落盘。</summary>
        void Flush();

        /// <summary>按时间范围查询。<paramref name="slaveId"/> 为 0 表示不限从站。</summary>
        IEnumerable<DeviceData> Query(DateTime from, DateTime to, byte slaveId);

        /// <summary>返回当前存储的记录总数。</summary>
        long Count();

        /// <summary>把指定日期之前的数据归档导出，并释放其占用的空间。返回导出的文件路径。</summary>
        string Archive(DateTime before);

        /// <summary>删除早于 <paramref name="retention"/> 的数据，返回删除的记录数。</summary>
        int Cleanup(TimeSpan retention);
    }
}
