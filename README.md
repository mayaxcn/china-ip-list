# Mainland China IP List (IPv4 + IPv6)

根据亚太互联网络信息中心（**APNIC**）的亚太地区 IP 地址分配列表，自动更新中国大陆 IP 数据。

每 **8 小时** 自动更新一次（UTC 00:00 / 08:00 / 16:00，对应北京时间 08:00 / 16:00 / 00:00）。
Auto-updated every **8 hours** (UTC 00:00 / 08:00 / 16:00).

> ⚠️ This project targets **.NET 7**, which is out of official support.
> The generated IP data is sourced from public APNIC statistics and provided as-is, without warranty.

## 数据来源

APNIC Delegated Statistics: <https://ftp.apnic.net/apnic/stats/apnic/delegated-apnic-latest>

## 输出文件

| 文件 | 内容 | 格式 |
|---|---|---|
| `chnroute.txt`    | IPv4 CIDR    | `<ip>/<prefix>` |
| `chn_ip.txt`      | IPv4 范围    | `<start_ip> <end_ip>` |
| `chnroute_v6.txt` | IPv6 CIDR    | `<ip>/<prefix>` |
| `chn_ip_v6.txt`   | IPv6 范围    | `<start_ip> <end_ip>` |

## 使用方式

**路由器 (OpenWrt) / 防火墙**：直接使用 CIDR 文件

- IPv4: <https://raw.githubusercontent.com/mayaxcn/china-ip-list/master/chnroute.txt>
- IPv6: <https://raw.githubusercontent.com/mayaxcn/china-ip-list/master/chnroute_v6.txt>

**其他客户端**：使用范围格式

- IPv4: <https://raw.githubusercontent.com/mayaxcn/china-ip-list/master/chn_ip.txt>
- IPv6: <https://raw.githubusercontent.com/mayaxcn/china-ip-list/master/chn_ip_v6.txt>

## 本地构建

```bash
dotnet build --configuration Release
dotnet run --project china_ip_list.csproj --configuration Release
```

输出文件会写入 `bin/Release/net7.0/`。

## 自定义更新频率

公开仓库在 GitHub Actions 上的最小触发间隔是 5 分钟。
如需更高频更新，请 fork 后修改 `.github/workflows/get_chn_ip.yml` 中的 `cron` 表达式，例如：

```yaml
- cron: '*/30 * * * *'   # 每 30 分钟一次
```

注意：APNIC 源站点的 delegated 列表**每天只更新若干次**，过高的轮询频率不会获得更及时的数据，反而会增加源站负担。

## License

MIT — see [LICENSE](./LICENSE). Generated IP data derives from the APNIC public registry.