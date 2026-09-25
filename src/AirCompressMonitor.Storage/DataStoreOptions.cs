using System;

namespace AirCompressMonitor.Storage
{
    /// <summary>存储模式。</summary>
    public enum StoreMode
    {
        /// <summary>只写 SQLite。查询快、占空间小，适合长期留档。</summary>
        Sqlite = 0,

        /// <summary>只写 CSV。纯文本、随手能打开、便于交给别人，适合现场排查。</summary>
        Csv = 1,

        /// <summary>两者都写。用 CSV 的可读性补 SQLite 的查询效率。</summary>
        Both = 2
    }

    /// <summary>存储层配置。</summary>
    public class DataStoreOptions
    {
        /// <summary>数据根目录。所有库文件与 csv 都在它下面。</summary>
        public string RootDirectory { get; set; }

        /// <summary>存储模式。</summary>
        public StoreMode Mode { get; set; }

        /// <summary>保留天数。超过的按天分表整体删除，默认 30 天。</summary>
        public int RetentionDays { get; set; }

        /// <summary>缓冲多少条触发一次落盘。</summary>
        public int BatchSize { get; set; }

        /// <summary>缓冲最长停留多久（毫秒），到点强制落盘，避免低频时数据一直挂在内存里。</summary>
        public int FlushIntervalMs { get; set; }

        /// <summary>归档目录名（相对根目录）。</summary>
        public string ArchiveFolderName { get; set; }

        /// <summary>SQLite 库文件名。</summary>
        public string DatabaseFileName { get; set; }

        /// <summary>CSV 目录名。</summary>
        public string CsvFolderName { get; set; }

        public DataStoreOptions()
        {
            RootDirectory = "Data";
            Mode = StoreMode.Both;
            RetentionDays = 30;
            BatchSize = 100;
            FlushIntervalMs = 5000;
            ArchiveFolderName = "archive";
            DatabaseFileName = "monitor.db";
            CsvFolderName = "csv";
        }

        public string DatabasePath
        {
            get { return System.IO.Path.Combine(RootDirectory, DatabaseFileName); }
        }

        public string CsvDirectory
        {
            get { return System.IO.Path.Combine(RootDirectory, CsvFolderName); }
        }

        public string ArchiveDirectory
        {
            get { return System.IO.Path.Combine(RootDirectory, ArchiveFolderName); }
        }

        /// <summary>把相对路径补成绝对路径，避免受工作目录变化影响。</summary>
        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(RootDirectory))
                RootDirectory = "Data";

            if (!System.IO.Path.IsPathRooted(RootDirectory))
                RootDirectory = System.IO.Path.GetFullPath(RootDirectory);

            if (RetentionDays < 1) RetentionDays = 1;
            if (BatchSize < 1) BatchSize = 1;
            if (FlushIntervalMs < 200) FlushIntervalMs = 200;
        }

        public DataStoreOptions Clone()
        {
            return (DataStoreOptions)MemberwiseClone();
        }
    }
}
