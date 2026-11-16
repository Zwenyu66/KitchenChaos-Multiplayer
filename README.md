# Kitchen Chaos：Mirror + KCP 完整工程

在原 Kitchen Chaos 游戏与现有真人联机版本上修改。Unity 版本保持 **2022.3.62f3**，保留原厨房、模型、动画、声音及蓝色 HUD。此次为完整源码工程，包含 Mirror v96.11.5 与 KCP 源码，无需手动导入网络插件。

## 本次六项修改

1. **单人开局**：创建房间后，房主自己点击“准备”，即可点击“开始本局”。也支持2至4人全员准备后开始，共用相同玩法。
2. **盘子可见**：盘子柜台按房主库存生成0至4个盘子的堆叠模型，拿走减少，自动补充时增加。
3. **配料顺序自由**：只校验食材种类、加工状态与数量。例如先放熟牛肉再放面包，同样能完成经典牛肉堡；双芝士仍要两份芝士。订单图标用“×2”等标明份数。
4. **Mirror + KCP 联机**：替换原 TCP 通道；本地移动预测、房主确认后重放未确认输入、远端位置平滑；移动走独立的不可靠通道，食材与订单走可靠通道；10Hz增量状态、30Hz移动和定期全量校正。
5. **面包只取一份**：带面包的配方只包含一个 Bread，因为该模型已有两片；不再要求首尾分别放面包。田园三明治对应调整为95元。
6. **原素材区分角色**：按房间位置使用原有黄色、红色、绿色、蓝色身体材质，保留眼睛和动画。玩家离开后空出的颜色可由新玩家复用。

之前的可关闭提示、左上角订单独立区域、蓝色 HUD、牛肉取料台、连续按F切菜、按菜品难度分别计价都保留。

## 放到指定目录

1. 关闭旧工程的 Unity 编辑器，将压缩包完整解压。
2. 在最外层 `KitchenChaos-Multiplayer` 中双击 `安装到指定目录.cmd`。
3. 脚本复制到 `E:\Users\HP\Desktop\大三课程\实训\KitchenChaos-Multiplayer`；该位置已有文件时新建带时间后缀的目录，保留旧工程。
4. Unity Hub 添加**新目录内层的 `KitchenChaos`**。选择的文件夹必须同时包含 `Assets`、`Packages`、`ProjectSettings`。

本次交付环境不能直接写入你的 Windows E 盘，以上脚本负责在你的电脑上复制。

## 打开与单人运行

1. 在 Hub 中用 **Unity 2022.3.62f3** 打开内层 `KitchenChaos`，等待资源导入和脚本编译。首次需要网络恢复原 Unity 包及 Newtonsoft JSON；Mirror 与 KCP 已在 Assets 中。
2. 执行菜单 `Kitchen Chaos → Validate Project`，然后 `Kitchen Chaos → Open Multiplayer Kitchen`。
3. 点击 Unity 顶部 Play，填写昵称，点击“创建房间”。
4. 点击“准备”，再点击“开始本局”。只有房主一人也可开始，三秒后进入300秒对局。
5. 也可从 `Assets/Scenes/MainMenuScene.unity` 的 PLAY 进入。

无需拖脚本、配置 NetworkManager、创建网络预制件或运行额外后端服务。工程通过 Mirror 自定义网络消息同步房主权威状态。

## 导出与多人运行

1. 在 Hub 为2022.3.62f3安装 **Windows Build Support (Mono)**。
2. 退出 Play，执行 `Kitchen Chaos → Build Windows Multiplayer`。
3. 输出位于 `KitchenChaos\Builds\Windows`。将**整个文件夹**发给所有玩家，不能只发 `KitchenChaos.exe`。
4. 双击 exe 运行。房主创建房间，其余玩家填写房主局域网 IPv4、默认端口 `7777`、六位房间码加入。
5. Windows 弹出网络访问请求时允许当前专用网络；房主需放行 **UDP 7777**。原 TCP 防火墙规则不能代替本次 UDP 规则。
6. 所有人准备后房主开始。本局开始后不接收新玩家；等结算后房主返回房间再加入。

本机双开：导出后双击 `双开测试.cmd`，第二个窗口地址填 `127.0.0.1`。不同电脑必须填写房主实际 Wi-Fi/以太网 IPv4，不能填127.0.0.1。

这次协议升级不兼容旧 TCP 版本。所有人使用同一次导出的完整文件夹最稳妥；仅复制代码到旧工程、只替换某个JSON、或一边运行旧exe都可能触发版本校验。

采用局域网/可达IP直连，没有公网房间服务或自动 NAT 穿透。异地玩家可使用同一虚拟局域网；普通公网直连需要房主具备可达地址及 UDP 端口映射。房间码不能代替IP。

## 操作和制作

| 按键 | 操作 |
|---|---|
| WASD / 方向键 | 移动 |
| Shift | 奔跑 |
| E | 取料、拿盘、放下、拿起、装盘、交付、丢弃 |
| F | 空手面对砧板切菜，每次按下切一次，可连续按 |
| H | 显示或隐藏本机提示 |
| Esc | 本机设置菜单；显示网络类型和往返延迟RTT，不停止队友的对局 |

拿盘后可放在空台面，再向盘子放入已加工食材；也可手拿盘子收集台面食材。蔬菜与芝士先切四次。牛肉在带“牛肉”标牌的取料台获取，炉灶煎8秒变熟，再过8秒烧焦。生肉、烧焦肉及未切蔬菜不能装盘。经典牛肉堡只需一份面包加一份熟牛肉，放入顺序任意。

同种食材多份时，以订单及手持栏的数量为准；原3D餐盘同种食材只显示一个对应造型。误交回收餐盘并扣10元；正确出餐按订单金额入账。更多菜品需要更多切配或煎肉，收入相应提高。

## 联机优化的范围

移动不再等待一次房主往返才显示：客户端先按相同碰撞规则移动，收到房主确认后校正并重放未确认输入。每个输入附带序号与最近8帧冗余，减少短时丢包造成的停顿。房主每个固定步长最多消费一帧，避免重复输入或密集发包加速角色。

拿取、放置、切菜、装盘、计分仍由房主裁决，保证物品不被重复拿走或重复计费。这类交互需要房主确认；网络插件不能消除真实网络往返耗时。查看 Esc 菜单中的RTT；优先用有线或稳定的同一Wi-Fi测试。

## 自动检查与验收

本次已实际完成 **30项 C# 回归测试**，其中包含真实 KCP UDP 双客户端、可靠大消息分片/中文内容、不可靠移动通道，以及模拟100ms单程延迟、乱序和20%输入消息丢失后的预测恢复。详见 `tests/运行结果.txt`。

另外通过自有运行时代码和编辑器工具的API接口替身编译检查，以及资源引用、GUID、配方和包结构检查。**当前环境没有 Unity 编辑器，尚未实测 Unity 导入、Mirror Weaver、Windows 导出或两台电脑游玩的画面与延迟；API接口替身编译不等同于Unity构建通过。** 按 `验证记录.md` 中的人工验收表在Windows上检查。

Unity菜单 `Kitchen Chaos → Run Regression Tests` 可重跑；或安装.NET 8 SDK后在最外层运行：

```powershell
dotnet run --project .\tests\Multiplayer.Tests.csproj
```

## 主要文件

| 文件/目录 | 职责 |
|---|---|
| `Assets/Multiplayer/Core/KitchenServer.cs` | 房主权威玩法、食谱、订单、收入、输入排队 |
| `Assets/Multiplayer/Core/Movement.cs` | 固定步长移动、输入冗余、本地预测与校正 |
| `Assets/Multiplayer/Core/WorldDelta.cs` | 食材与设备状态增量同步 |
| `Assets/Multiplayer/Runtime/MirrorLink.cs` | Mirror + KCP 通道、消息和连接身份 |
| `Assets/Multiplayer/Runtime/MultiplayerSession.cs` | 握手、版本、开局、输入与状态调度 |
| `Assets/Multiplayer/Runtime/NetworkWorldView.cs` | 原素材角色颜色、物品显示、输入和碰撞 |
| `Assets/Scripts/Counters/PlatesCounterVisual.cs` | 根据同步库存更新可见盘堆 |
| `Assets/Resources/MultiplayerRules.json` | 单人开局阈值、配方与差异价格 |
| `Assets/Mirror` | 随工程附带的官方网络插件源码及许可证 |

玩法参数修改后重新导出，并把同一份完整构建给所有玩家。代码版本指纹规范化Windows换行符，避免Git的LF/CRLF转换导致相同源码误判。
