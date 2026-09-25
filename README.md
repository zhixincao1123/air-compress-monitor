# AirCompressMonitor（空压机联网监控）

工业空压机（螺杆机）的数据采集与远程监控上位机。C# / .NET Framework 4.7.2，Modbus RTU/TCP 直连 PLC，
不依赖任何组态软件。

仓库里是**两个可独立运行的宿主**，共用同一套通信层与存储层：

| 宿主 | 类型 | 干什么 |
| --- | --- | --- |
| `src/AirCompressMonitor.Wpf` | WPF | 上位机主程序。实时监控（自绘仪表盘）、远程控制、报警管理、故障诊断、从站管理 |
| `src/AirCompressMonitor.Tools` | WinForm | 现场排障工具集。从站扫描、寄存器监视、采集落盘、通信诊断 |

## 目录结构

```
src/AirCompressMonitor.Comm/        通信层（无 UI 依赖，可单独引用）
  Modbus/IModbusLink.cs             链路抽象：串口 / TCP 两种实现同一接口
  Modbus/SerialRtuLink.cs           RS232 / RS485 串口链路
  Modbus/TcpLink.cs                 Socket / TCP 链路
  Modbus/Endianness.cs              大小端还原
  Modbus/EndianDetector.cs          按数值合理性自动判定字节序
  Modbus/ModbusMonitorClient.cs     多从站轮询、心跳保活、断线重连
  Profiles/PlcBrandProfile.cs       多品牌 PLC 适配模板（8 家，含默认字节序与换算系数）
  Diagnostics/RuleBasedExceptionAnalyzer.cs   异常 → 原因 → 处置建议
src/AirCompressMonitor.Storage/     存储层
  DataStoreHub.cs                   门面：攒批、归档、清理节拍
  SqliteDataStore.cs                按天分表 + 批量事务 + WAL
  CsvDataStore.cs                   按天分文件，带 BOM
src/AirCompressMonitor.Wpf/         上位机主程序（MVVM）
  Control/Meter.xaml.cs             自绘仪表盘（刻度、指针、量程都是画出来的，无第三方图表库）
  ViewModel/MainViewModel.cs        四个页面的状态与命令
src/AirCompressMonitor.Tools/       工具集（WinForm）
tests/AirCompressMonitor.SmokeTest/ 冒烟测试，43 项
build/ThirdParty.References.targets 第三方引用集中处（见下）
packages/                           离线依赖包（随仓库走，不联网也能编）
```

## 构建

**本机没有装 Visual Studio，也不要用 `dotnet build` / `dotnet msbuild`** ——
`dotnet msbuild` 缺 `Microsoft.WinFX.targets`，构建 .NET Framework 的 WPF 项目会直接失败。

用 Framework 自带的 MSBuild，编译器指到 Roslyn（Framework 自带的 csc 不支持 `LangVersion` 较高的写法）：

```bash
MSYS_NO_PATHCONV=1 "/c/Windows/Microsoft.NET/Framework64/v4.0.30319/MSBuild.exe" \
  AirCompressMonitor.sln -v:m -nologo -p:Configuration=Debug \
  -p:CscToolPath='C:\Program Files\dotnet\sdk\10.0.301\Roslyn\bincore' -p:CscToolExe=csc.exe
```

`CscToolPath` 里的 SDK 版本号随机器不同，按实际安装的改；只影响编译速度与语法特性，不影响产物。

每次构建前先清干净，避免增量构建把旧产物混进来：

```bash
find src tests -type d \( -name obj -o -name bin \) -exec rm -rf {} +
```

构建时会刷一条 `项目文件包含 ToolsVersion="15.0"…` 的警告，无害，可以
`| grep -v 'ToolsVersion="15.0"'` 滤掉。**MSBuild 的报错正文是 OEM 编码的，在 Git Bash 里是乱码**，
按行号定位即可，别指望读中文错误信息。

### 为什么要 `build/ThirdParty.References.targets`

Framework 版 MSBuild **不会把 ProjectReference 的 CopyLocal 依赖透传到输出目录**：
库项目引的第三方 dll 编译期一切正常，但不会出现在 exe 的输出目录里，表现是**编译全绿、运行期
`FileNotFoundException`**。所以每个 exe 项目都必须自己再引一遍，统一放在这个 targets 里，
在 `<Import ... Microsoft.CSharp.targets>` **之前**导入。

HintPath 一律用 `$(MSBuildThisFileDirectory)` 拼绝对路径 —— HintPath 是相对**项目目录**解析的，
写相对路径会随导入位置变化而失效。

### 版本坑

引用 netstandard2.0 版的 SQLitePCLRaw，在 .NET Framework 上要补齐 `System.Memory` 一系，
而 **NuGet 包版本 ≠ 程序集版本**，绑定重定向必须按程序集版本写：

| 包版本 | 程序集版本 |
| --- | --- |
| system.memory 4.5.4 | 4.0.1.1 |
| system.buffers 4.5.1 | 4.0.3.0 |
| system.runtime.compilerservices.unsafe 6.1.2 | **6.0.3.0**（它的 netstandard2.0 版是 6.0.0.0，别拿错） |
| system.numerics.vectors 4.6.1 | 4.1.6.0 |

## 运行

**必须 x64。** 两个 exe 都设了 `<PlatformTarget>x64</PlatformTarget>` + `<Prefer32Bit>false`，
因为随程序部署的 `e_sqlite3.dll` 是 x64 的；跑成 32 位进程会在第一次打开数据库时报「找不到本机库」。

```bash
src/AirCompressMonitor.Wpf/bin/Debug/AirCompressMonitor.Wpf.exe
src/AirCompressMonitor.Tools/bin/Debug/AirCompressMonitor.Tools.exe
```

SQLite 原生提供程序在 `SqliteDataStore.EnsureNativeProvider()` 里显式指定 `e_sqlite3`，
刻意不走 `SQLitePCLRaw.Batteries_V2.Init()` —— 后者在 .NET Framework 上会选 `dynamic_cdecl` 分支，
而那个 provider 包不在引用集合里，运行期才炸。

## 冒烟测试

```bash
cd tests/AirCompressMonitor.SmokeTest/bin/Debug && ./AirCompressMonitor.SmokeTest.exe
```

43 项，不连真实设备，覆盖：字节序换算与自动判定、规则库诊断、以及**存储层能否真的落盘并读回**
（含仅 SQLite 模式下的归档与超期清理）。退出码 0 = 全过。

之所以必须有它：SQLite 的位数问题、归档目录不清理这类缺陷，**编译期一律看不出来**，
只能在运行期兜住。

## 设计取舍（都是踩过坑才定下来的）

**串口是独占资源。** 同一时刻只允许一个会话持有 COM 口。工具集的三个页面共用「从站扫描」页上的
一份链路配置，切页时主动让出端口：开始采集前会先释放监视会话，采集运行期间拒绝扫描。
否则第二次 `Open()` 会报「串口被占用」，而现场工程师会把它误读成线缆坏了。

**串口超时要在 `Open()` 之前设。** `SerialPort` 在 `Open()` 那一刻读走超时值，之后再改不生效；
`ModbusMonitorClient` 在 `CreateMaster()` 之后还要在 `master.Transport` 上再设一次。

**工具集界面用 `Panel` 而不是 `FlowLayoutPanel`。** 手写的 Designer 给每个子控件设了绝对坐标，
而 `FlowLayoutPanel` **忽略子控件的 `Location`**，会自己重排换行 —— 表现为第二行的按钮被推出可视区、
右边的控件直接看不见。改成普通 `Panel` 后布局精确，代价是窗口太窄会裁掉右侧，
所以设了 `MinimumSize = 900×560` 兜住设计宽度（984 减边框内边距剩 870，最右控件止于 823）。

**故意不加 app.manifest（不做 DPI 感知）。** 本机显示器是 200% 缩放。不做 DPI 感知时 Windows 会
把界面按位图拉伸，**字发虚但版面比例是对的**；一旦声明 DPI 感知，绝对坐标会按字体比例放大
（823 → 1646），直接冲出 984 宽的窗口。两害相权取其虚。

**CSV 一律带 UTF-8 BOM。** 归档出来的 CSV 是给人看的，多半直接用 Excel 打开，不带 BOM 中文是乱码。

**归档目录也要按保留期清理。** `Archive` 会把老表导出成 `archive/yyyy-MM-dd.csv` 再 DROP 表，
这些文件不在任何表里。只清表的话归档目录会无限膨胀 —— 而且在**仅 SQLite** 模式下没有 CSV 后端
帮忙扫这个目录，保留期就形同虚设。现在两个后端都清同一批文件（重复删不存在的文件无害）。

**双模式下归档文件会互相覆盖。** 两个后端都导出到 `archive/<日期>.csv`，后写的把先写的删掉重写。
内容相同（两边存的是同一批数据），所以无害，冒烟测试也过。**不要把 SQLite 的导出改名成
`<日期>.sqlite.csv`** —— `CsvDataStore.Cleanup` 按 `yyyy-MM-dd` 解析文件名，改名后永远清不掉，
比覆盖更糟。

**WinForms 的 DataGridView 绑不了字段。** 绑定走 `TypeDescriptor.GetProperties()`，只认属性，
行类用 public 字段会静默失败。工具集的表格一律用非绑定模式（`dgv.Rows.Add(...)`），
反正行本来就是清空重建的。

**采集落盘放在采集线程上做。** 存储是 I/O 密集的，塞进 UI 线程会把界面拖住。

## 未做的事

- `App.config` 里可能残留调试用的第三方 API Key —— 交付前必须轮换并改走环境变量。
- 没有自动化测试覆盖 UI 与真实串口；冒烟测试只覆盖纯软件部分。
