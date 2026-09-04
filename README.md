# SyncClipboardWin 0.3.0 - Windows 10 built-in compiler edition

这是从 0.1.7 的 .NET 8 Self-Contained 版本迁移来的轻量版。

## 目标

- 不需要安装 .NET 8 SDK。
- 不需要 `dotnet publish`。
- 使用 Windows 自带的 .NET Framework C# 编译器：
  - `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
  - 如果没有 64 位编译器则回退到 `Framework\v4.0.30319\csc.exe`
- 强制 `/langversion:5`，源码避免依赖新版 C# 语法。
- 最终 EXE 不再捆绑整套 .NET 8 Runtime，因此体积会从约 100MB 大幅下降。

## 编译

双击：

```text
build-win10.bat
```

成功后根目录生成：

```text
SyncClipboardWin.exe
```

不再使用 `.csproj`、`dotnet build` 或 `dotnet publish`。

## 便携配置

配置固定保存在 EXE 同目录：

```text
SyncClipboardWin.exe
config.json
```

第一次运行没有 `config.json` 时会打开设置窗口。设置保存后会在 EXE 同目录创建 `config.json`。

0.3.0 起 `config.json` 内可以保存多个配置；从 0.2.3 或更早版本升级时，旧的单配置会自动迁移为“默认配置”，原有 WebDAV、账号、快捷键等内容无需重填。

把 EXE 和 config.json 一起复制到其它 Windows 10 电脑即可继续使用。

## 保留的 0.1.7 功能

- WebDAV Basic Auth。
- SyncClipboard.json + /file/ 协议。
- 资源管理器选中文件上传。
- 桌面选中文件读取。
- 选中文字后直接使用上传快捷键。
- 上传快捷键只处理当前选中内容，不会回退上传旧剪贴板。
- 下载文本直接粘贴。
- 下载文件到当前资源管理器目录；非资源管理器时使用 Windows 实际“下载”Known Folder 下的 synccliptmp。
- Group ZIP 上传/下载解压。
- 剪贴板变化监测自动上传。
- 重复剪贴板内容排除。
- 自动上传忙碌期间后续复制直接忽略、不排队。
- 全局快捷键。
- 开机启动。
- 托盘图标和设置窗口。
- EXE 同目录便携配置。

## 迁移中的兼容改造

.NET 8 API 已替换为 .NET Framework / Windows 自带实现：

- `System.Text.Json` -> `System.Web.Script.Serialization.JavaScriptSerializer`
- `HttpClient` -> `HttpWebRequest`
- `SHA256.HashData()` -> `SHA256.Create().ComputeHash()`
- `Convert.ToHexString()` -> 手动 HEX
- `Path.GetRelativePath()` -> `Uri.MakeRelativeUri()`
- ZIP 覆盖解压 -> 手动遍历 `ZipArchiveEntry`
- `ApplicationConfiguration.Initialize()` -> WinForms 传统初始化
- 去除 nullable reference、record struct、using var、range、target-typed new 等新版语法

## 依赖

仅引用 Windows/.NET Framework 自带程序集：

```text
System.dll
System.Core.dll
System.Drawing.dll
System.Windows.Forms.dll
System.Web.Extensions.dll
System.IO.Compression.dll
System.IO.Compression.FileSystem.dll
Microsoft.CSharp.dll
```

没有 NuGet 包。

## 注意

1. 配置中的 WebDAV 密码仍是明文，这是为了让 EXE + config.json 可以跨电脑直接复制使用。
2. 不建议把程序放进 Program Files，因为便携配置需要写 EXE 同目录。
3. 这一版面向 Windows 10 自带 .NET Framework 环境。非常早期、被精简过或损坏的 Windows 镜像如果缺少 Framework 组件，编译脚本会直接提示。
4. 源码已按 Windows 自带 `csc.exe` 的 C# 5 语法约束重写；当前生成环境不是 Windows，因此最终仍建议在目标 Win10 上运行一次 `build-win10.bat` 做实际编译验证。


## 0.2.1

- 新增上传/下载进度窗口。
- 进度窗口固定在绝对屏幕坐标 `(0, 0)`，即主显示器左上角。
- 窗口置顶且不显示任务栏按钮，传输完成或失败后自动关闭。
- 上传/下载显示文件名、百分比、已传输大小/总大小。
- WebDAV 未返回 Content-Length 时，下载自动使用不确定进度动画并显示已下载大小。
- Group 上传在压缩阶段显示“准备压缩”，SHA256 阶段显示“计算校验”，实际上传后显示百分比。
- 文本上传也会短暂显示进度。
- 剪贴板自动上传同样显示进度窗口；忙碌期间新复制仍按原逻辑直接忽略。


## 0.2.2

- 修复 0.2.1 源码包中 `ProgressForm.cs` 与 `TransferProgress.cs` 被错误写成单行文本的问题。
- 修复其中的字面量 `\\n` 和 `\\"`，恢复为正常 C# 源码换行和引号。
- 功能逻辑不变：上传/下载进度窗口仍固定在绝对屏幕坐标 `(0,0)`。


## 0.2.3

- 修复 0.2.1/0.2.2 加入进度窗口后导致资源管理器/桌面选中文件读取失效的问题。
- 原因：旧实现先显示进度窗口，再调用 UploadAsync/DownloadAsync；进度窗口因此成为前台窗口，程序已经无法知道用户原来在哪个 Explorer 窗口选了什么。
- 现在改为：先读取当前 Explorer/Desktop/选中文本，只有真正进入压缩、校验、上传或下载阶段后，第一次收到进度数据时才创建进度窗口。
- ProgressForm 同时加入 `WS_EX_NOACTIVATE` 和 `ShowWithoutActivation`，即使显示也不主动抢走当前前台窗口。
- ExplorerHelper.cs、ClipboardHelper.cs 的 0.2.0 选取逻辑没有改动。


## 0.3.0

- 新增多配置：一个 `config.json` 中可保存多个独立配置。
- 设置界面可新建、复制、删除配置，并直接修改配置名称。
- 托盘菜单新增“切换配置”，可一键快速切换；当前配置带勾选标记。
- 切换配置后立即应用该配置的 WebDAV 地址、账号、密码、上传/下载快捷键、大小限制、通知和剪贴板监测设置。
- 兼容旧配置：0.2.3 及更早的单配置 `config.json` 会自动迁移为多配置结构。
- 新增“网络自动切换”总开关，每个配置可以独立决定是否参与自动匹配。
- 自动匹配条件支持：网络类型（Wi-Fi / 有线 / 其他）、IPv4 范围、网络名称。
- 网络名称会检查当前活动网卡名称、网卡描述以及 Wi-Fi SSID，使用不区分大小写的“包含”匹配。
- IPv4 范围支持：CIDR（`192.168.1.0/24`）、通配符（`192.168.1.*`）、起止范围（`192.168.1.10-192.168.1.99`）和单个 IP。
- 一个配置内部可选择“全部已填写条件同时满足”或“任一已填写条件满足”。
- 多个配置同时匹配时按设置中的配置列表顺序取第一个，以避免切换结果不确定。
- 网络环境每 5 秒检查一次；传输进行中不会切换配置。
