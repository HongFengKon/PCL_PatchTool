# PCL 下载功能修复工具（PCL DownloadFixer）

用来恢复 PCL（Plain Craft Launcher 2）在 `2.99.0.0 (413)` 这类构建里被移除的
**MC 下载功能**：下载游戏、整合包安装、本地导入整合包。

- 单文件 GUI 工具：选择 PCL 程序 → 点「开始修复」即可
- **不依赖老版本 PCL**：所有修补代码按目标程序自身的 IL 语义现场生成
- 只改 3 个方法，其余字节原样保留（用 Mono.Cecil 读写 IL，不重新编译）
- 写盘前回读校验，不通过就放弃替换，原文件不动

> 本工具仅修改你本地的可执行文件，用于恢复你原本就有的功能入口，不包含、
> 也不下载任何游戏资源。

---

## 快速开始

### 直接使用

1. 双击 `PCL下载修复工具.exe`
2. 点【选择程序...】，选中 `Plain Craft Launcher 2.exe`
3. 点【开始修复】，日志出现 `=== 完成` 即成功
4. 启动 PCL，点左侧【下载】

原文件会自动备份为 `Plain Craft Launcher 2.exe.bak`。

### 命令行

```bat
PCL下载修复工具.exe "D:\path\to\Plain Craft Launcher 2.exe"
```

退出码 `0` = 成功，`1` = 无需修复 / 不是目标版本，`2` = 异常。

### 自己编译

需要 Windows 自带的 .NET Framework 编译器（Win10/11 默认就有）：

```bat
build.cmd
```

产物在 `dist\PCL下载修复工具.exe`。也可以用 Visual Studio 打开 `PCLDownloadFixer.csproj`。

---

## 原理

被"禁用下载"的 PCL 并没有删掉下载引擎（`PCL.ModDownloadLib` 是完整的），
而是改写了三个入口。工具按下面的方式还原：

| 方法 | 被改成 | 还原为 |
|---|---|---|
| `PCL.PageDownloadInstall.LoaderInit` | `new LoaderTask("关闭下载", Sub() Throw New Exception("为遵守相关法律法规…"))` | `ldsfld PCL.ModDownload::DlClientListLoader` |
| `PCL.ModModpack.ModpackInstall(String,String,String)` | 类型识别后本该 `switch` 分发，被整段替换成 `throw` | 生成 10 路 `switch`，按类型调用 `InstallPackCurseForge / HMCL / MMC / MCBBS / Modrinth / LauncherPack / Compress` |
| `PCL.ModModpack.ModpackInstall()` | 只 `Hint` 一句提示 | 文件选择框（`Dialogs::SelectFile`）+ `ModBase::RunInThread` 后台导入 |

细节见 [`docs/技术说明.md`](docs/技术说明.md)：包括不用老版如何推导分发映射、
生成的 IL 结构、以及自定义的校验逻辑。

## 安全设计

写入前先输出到 `<原名>.pclfix.tmp`，再**重新读回来校验**：

1. 三处补丁是否真的生效（`ldsfld` / `switch` / `SelectFile` 调用存在）
2. 全程序集结构自检：异常处理表区间有序（`TryStart ≤ TryEnd`、
   `HandlerStart ≤ HandlerEnd`）、所有分支/switch 目标都落在方法体内

校验失败就删除临时文件、不替换原文件。已经修好的文件或老版本会被判定为
「本来就好」，**文件哈希不变**。

## 已知限制

- 针对 `2.99.0.0 (413)` 这类构建；更旧的版本本来就有下载，会被判为「本来就好」
- PCL 自带「检查更新」若把 exe 覆盖回官方版，需要对新 exe 重新跑一次
- 整合包分发依赖目标文件自身"类型识别"代码里出现的标记文件名
  （`mcbbs.packmeta` / `mmc-pack.json` / `modrinth.index.json` /
  `manifest.json` / `modpack.json` / `modpack.zip` / `modpack.mrpack`）。
  如果哪天 PCL 大改这块，工具会报「跳过」而不是乱改

## 目录结构

```
PCL_DownloadFixer/
├─ src/Program.cs              全部源码（GUI + 补齐逻辑）
├─ lib/Mono.Cecil.dll          内嵌进 exe 的 IL 读写库（MIT）
├─ docs/技术说明.md            原理与实现细节
├─ build.cmd                   一键构建
├─ PCLDownloadFixer.csproj     Visual Studio 工程
├─ LICENSE                     MIT
└─ README.md
```

## 第三方组件

- [Mono.Cecil](https://github.com/jbevain/cecil) 0.11.6（net40 构建）— MIT License，
  作为嵌入资源打包进 exe，运行时从内存加载。

## License

MIT，见 [LICENSE](LICENSE)。
