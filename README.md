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


## 0.3.1

- 网络自动切换新增“默认回退配置”。
- 可在任意一个配置中勾选“此配置作为默认回退配置（其他规则都不匹配时使用）”。
- 自动切换时先按原有配置顺序检查所有普通匹配规则；只有全部未命中时，才切换到默认回退配置。
- 默认回退配置不会参与普通规则匹配，因此不会提前抢占后续配置。
- 默认回退配置全局只能有一个；选择新的默认项时会自动取消之前的默认项。
- 未设置默认回退配置时，行为与 0.3.0 完全一致：全部规则不匹配则保持当前配置不变。


## 0.3.2：局域网直传 + WebDAV 兜底

新增“启用局域网点对点文件传输（WebDAV 兜底）”开关。默认关闭，关闭时行为与 0.3.1 一致。

开启后，文件上传流程变为：

1. 发送端计算文件/压缩包 SHA-256，但暂不上传文件到 WebDAV。
2. WebDAV 根目录写入 `SyncClipboard.direct.json`，记录 `transferId`、随机 token、发送端 IPv4/掩码、TCP 端口、文件名、大小、hash、类型与状态。
3. 标准 `SyncClipboard.json` 临时写成 `Text` 类型兼容提示，并包含本次 `transferId`。旧版 SyncClipboard 客户端仍可正常读取，不会去下载一个尚不存在的文件。
4. 新版客户端读取云剪贴板时，如果发现两份元数据的 `transferId` 相符，会根据 IP 与掩码判断是否可能处于同一局域网；若是，则优先连接发送端 TCP 端口直接收文件。
5. 直传成功后校验 SHA-256，然后按原来的 File / Image / Group 流程粘贴或解压。
6. 如果不在同一局域网、TCP 无法连接、连接中断或 hash 校验失败，接收端会把 `SyncClipboard.direct.json` 状态改为 `upload_requested`。
7. 发送端后台每 2 秒检查一次当前直传任务；看到 `upload_requested` 后才把文件上传至 WebDAV，并把标准 `SyncClipboard.json` 恢复成原来的文件元数据。接收端最多等待 60 秒后改走标准 WebDAV 下载。

### TCP 直传

默认端口 `45678`，每个配置可单独设置。监听地址为所有本机 IPv4 接口。Windows 防火墙必须允许该程序/端口被局域网访问。

TCP 请求使用当前 `transferId` + 随机 token 验证。token 只写入经过 WebDAV 账户认证才能读取的 `SyncClipboard.direct.json`，不会写进兼容提示文本。

### 取消传输

传输进度窗口新增“取消传输”按钮；托盘菜单也新增“取消当前传输”。取消会停止当前 HTTP/TCP 数据流；如果仍处于“等待局域网直传”阶段，则同时把 direct 状态标记为 `cancelled` 并释放为压缩而保留的临时文件。

### 注意

- 这是兼容扩展协议，额外文件名固定为 `SyncClipboard.direct.json`。
- 同一个 WebDAV 配置当前只维护一个最新直传任务，新的文件会替换旧的待直传任务，这与原 SyncClipboard “云剪贴板只有当前一份内容”的语义一致。
- 如果发送端已经退出，而文件尚未上传到 WebDAV，接收端无法凭空取得该文件；此时会在等待 60 秒后提示发送端未响应。
