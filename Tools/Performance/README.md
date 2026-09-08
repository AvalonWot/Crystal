# Cell 动态性能验证

该控制台宿主运行当前工作区的 `Server.Library`（Release/net8.0），每秒记录 CPU、工作集、托管内存、分配、GC 和服务器对象循环时间。采样结束后还会核对地形校验和、动态格子索引与全局对象表。

## 运行

必须使用服务器运行环境的独立副本。服务器启动、保存及退出会写入数据库和配置，不要直接指向正式环境或原始 `Build/Server/Debug`。

```powershell
dotnet build Tools/Performance/Performance.csproj -c Release -o Build/CellPerformance/host
$env:PERF_SECONDS = '180'
$env:PERF_PORT = '17000'
dotnet Build/CellPerformance/host/Performance.dll Build/CellPerformance/runtime Build/CellPerformance/multi.jsonl
```

传入 `single` 可覆盖 `Multithreaded=false`：

```powershell
dotnet Build/CellPerformance/host/Performance.dll Build/CellPerformance/runtime-single Build/CellPerformance/single.jsonl single
```

- `PERF_SECONDS` 默认 150 秒。
- `PERF_PORT` 默认 17000；若端口被占用，可换用其他空闲端口。
- 宿主固定监听 `127.0.0.1`，关闭 HTTP、状态端口和客户端版本检查。
- `Summarize.ps1` 默认取第 30–140 秒，避开地图加载和结尾普查。
- `LastRunTime` 是服务器已有的对象遍历周期，不是玩家请求延迟。

## Cell 配置

服务器首次保存设置时会在 `Setup.ini` 写入：

```ini
[Performance]
CellObjectPoolMaxRetainedCount=4096
```

该值是 4、8、16 三个容量档合计保留的空闲容器个数。缺失时默认 4096；0 或负数关闭缓存。修改后需重启服务端生效。

## 诊断

```powershell
Tools/Performance/Summarize.ps1 Build/CellPerformance/multi.jsonl
dotnet tool install dotnet-trace --tool-path Tools/Performance/bin
Tools/Performance/bin/dotnet-trace collect -p <PID> --profile dotnet-sampled-thread-time,gc-collect --duration 00:00:30 -o Build/CellPerformance/multi.nettrace
Tools/Performance/bin/dotnet-trace convert Build/CellPerformance/multi.nettrace --format Speedscope
Tools/Performance/AnalyzeTrace.ps1 Build/CellPerformance/multi.speedscope.json
```

原始运行数据保存在被 Git 忽略的 `Build/CellPerformance`，其中包含复制的业务数据，不应提交。
