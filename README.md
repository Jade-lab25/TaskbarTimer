# TaskbarTimer 任务栏计时器

一个 Windows 端轻量计时小工具：计时小窗贴靠在任务栏时钟旁实时显示，支持正计时 / 倒计时、悬停查看任务名、每日任务数与时长统计。

## 功能特性

- **任务栏计时小窗**：无边框置顶小窗贴靠在任务栏时钟左侧，实时显示 `时间 + 任务名`
- **正计时 / 倒计时**：倒计时支持 25 / 45 / 90 分钟预设，归零后红闪 + 提示音 + 任务栏图标闪烁提醒；正计时可选 25 / 45 / 60 / 90 分钟目标，到点温和提醒
- **注意力巡检**：计时中随机弹出「还在做【任务名】吗？」（默认 3–8 分钟随机），主动把分心的你拉回来；不抢焦点、不打断打字，5 秒无操作自动收起
- **切换任务**：计时中无缝切到另一任务，前后两段记录首尾相接、历史不被改写
- **分心计数**：随时标记「我分心了」，统计每日分心次数，CSV 一并导出
- **悬停查看**：鼠标悬停小窗显示任务名、模式、状态、开始时间
- **每日统计**：主窗口表格展示日期 / 开始 / 结束 / 时长 / 专注事项 / 分心次数 / 进展记录，支持今天 / 近 7 天 / 全部筛选；任务名与进展记录可双击编辑
- **数据导出**：一键导出 CSV（UTF-8 带 BOM，WPS 打开不乱码）
- **历史复用**：双击历史记录或「再次计时」按钮快速复用任务名
- **系统托盘常驻**：关闭主窗口只是隐藏，可从托盘图标随时唤回；再次启动自动唤醒已运行实例

## 小窗操作

| 操作 | 行为 |
|------|------|
| 单击 | 暂停 / 继续 |
| 双击 | 打开主窗口 |
| 右键 | 菜单（开始/暂停 / 切换任务 / 我分心了 / 结束并保存 / 打开主窗口 / 退出） |
| 拖动 | 移动位置并自动记忆，超出工作区自动归位 |

## 技术栈

- C# / WPF，.NET 8
- 数据存储：本地 JSON（`%APPDATA%\TaskbarTimer\records.json`）

## 构建与分发

```bash
# 本机版（依赖 .NET 8 桌面运行时，体积小）
dotnet publish -c Release -o publish

# 自包含版（免装运行时，适合分发）
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-self-contained
```

> 自包含版是**真正的单文件**：已在 csproj 固化 `IncludeNativeLibrariesForSelfExtract=true`，把 WPF 原生库（`wpfgfx_cor3.dll` 等）也打包进 exe，可直接分发单个 `TaskbarTimer.exe`。

### 分发方式

直接把 `publish-self-contained\TaskbarTimer.exe`（约 68MB）发给对方，双击即用，免装 .NET 运行时。`TaskbarTimer.pdb` 是调试符号，不用发。

## 数据存储位置

- 记录：`%APPDATA%\TaskbarTimer\records.json`
- 设置（小窗位置等）：`%APPDATA%\TaskbarTimer\settings.json`

## License

[MIT](LICENSE)
