# Creek Beautiful Corner / 模块化环境美术

2026-11-13：五关新增草花/补给组合、两层松林浮岛背景，以及仅桌面使用的渐变天空。XR 保留透视背景，不增加深度纹理或透明雾。素材目录、性能预算和截图见 [拼车场景美术](Car-Campaign-Scenery.md)。

## 当前美术分支整合状态（2026-11-12，合并 /main cs167）

程序最新的操作区、玩家起点、环境/赛道摆放和 Quest 渲染配置已合并。固定 1.0x 眼缓冲、Mobile 2× MSAA、动态分辨率关闭、Depth/Opaque Texture 关闭，保留 Meta SDK High 动态注视点、Normal 锐化和 90 Hz。

在最新布局上重新应用每关美术、道路净空和轻量范围框，保留独立手工摆放素材。`ShowWalls` 现在控制轻量边框，拼装时开启、开跑后隐藏；下方历史记录中“默认关闭/恢复透明墙”的描述不再适用于本美术分支。范围尺寸和玩法判定保持程序版本。

`Living river`、`Modular river` 和第五关 `Freight river` 全部关闭深度及场景颜色采样。水面 shader 新建材质也默认关闭这两个独立开关；保留动态水波、焦散及我们的导数过滤。当前无需相机深度/颜色贴图即可显示，但不会自动获得真实水深岸线泡沫或场景折射。预览及运行截图工具同样不强制生成相机贴图。

保留程序的传送门 AlphaToMask 和不透明深度写入，同时合并内部纹理抗锯齿；方向盘虚线使用避开角度接缝的导数，水面合并细泡沫淡出与原有噪声过滤。保留 `QuestRenderAB` live 模式，新范围框也参与 `boundary_all_off`。

Unity 6000.3.21f1：56/56 EditMode、2/2 PlayMode 通过，实际加载五关并检查动画。未重新构建 Android 或测试真机；下方同事的性能测量是 cs167 带入的记录，不是本次复测。旧 BoundaryReview APK 来自 cs166，需要重新构建后才可评估本次整合版本。

新截图：[第二关](Car-Campaign-Merged-Level2.png)、[第五关](Car-Campaign-Merged-Level5.png)。重放整合入口为 `PhysicalAssembly.Editor.CarSceneryIntegration.Apply`；会重建自动美术分组，手工调整前请另存实例或 Variant。详细素材说明见 [手工摆放和抗锯齿](Manual-Scenery-and-Shader-AA.md)。

根据参考图制作的原创低多边形溪谷沙盘。当前交付是独立的 beautiful corner 美术样板和可自由摆放的模块。桥的展示路线仅用于构图，实际路线、障碍、台阶和碰撞由关卡作者安排。

## 打开与交接

- `Assets/CreekDiorama/Scenes/BeautifulCorner.unity`：起点、终点、木桥、岩岸、松树、动态溪水、瀑布、落水泡沫和可选传送门的独立展示场景。
- `Assets/CreekDiorama/Prefabs/BeautifulCorner.prefab`：完整美术样板；摄影相机、灯光和展示地面不包含在此 Prefab 中。
- `Assets/CreekDiorama/Scenes/CreekModuleGallery.unity`：20 个模块的实际 Prefab 实例陈列。
- `Assets/CreekDiorama/Prefabs/Modules/`：可拖入关卡的独立模块，均无 Collider、无运行时行为，不依赖玩法资产。
- `ArtSource/CreekDiorama/CreekModularKit.blend`：模块的 Blender 4.3 可编辑源文件，部件按行摆开。
- `ArtSource/CreekDiorama/CreekDiorama.blend`：原始完整沙盘源文件，保留分组。
- `BeautifulCorner-Botanical.png`、`Botanical-WaterfallDetail.png`：植物与岩石升级后的 Unity 实际预览；`BotanicalGallery.png` 展示新装饰模块，`ModuleGallery.png` 展示基础桥梁模块。原始 `BeautifulCorner.png` 保留用于对照。

## 模块尺寸与摆放

单位为米，默认前进方向为局部 +Z，桥面宽 0.40 m。建议使用 0.20 m 基础网格，精确连接时对齐 Anchor_Entry 与上一块的 Anchor_Exit；这些空物体只标记位置，不包含逻辑。桥面顶面入口位于 y=0，可整体缩放。

| 模块 | 尺寸 / 用途 |
| --- | --- |
| Deck_Straight_040 / 080 | 长 0.40 / 0.80，宽 0.40 |
| Deck_Ramp_080_Rise020 | 长 0.80，上升 0.20 |
| Deck_Stairs_080_Rise020 | 长 0.80，上升 0.20，共 8 级 |
| Stair_Tread_040 | 单级踏板，宽 0.40、深 0.10 |
| Deck_Turn90_080 | 0.80 × 0.80 占地；入口 (0,0,0)，出口 (0.60,0,0.60)，出口朝向 +X |
| Support_Pier_020 / 040 / 060 | 桥墩高度 0.20 / 0.40 / 0.60，含 Foot / Deck 对接点 |
| Railing_040 | 长 0.40 的独立绳栏杆 |
| Landing_Platform_080 | 0.80 × 0.80 木平台 |
| Marker_Start / Goal | 蓝色起点、红色终点标牌 |
| Portal_Frame | 木门框与可单独隐藏的青蓝漩涡 |
| Water_Tile_080 | 0.80 × 0.80 水面 |
| Waterfall_020x040 | 宽 0.20、高 0.40，含 Lip / Impact 对接点 |
| Cliff_Terrace_040 | 0.40 岩岸草地模块 |
| Station_Start / Build / Goal | 起点、拼车平台与终点组合模块 |

Station_Build 是 Station_Start 的 **Prefab Variant**，共用同一木平台、门框、蓝色标牌和材质；修改基础起点 Prefab 会同步到拼车平台。Anchor_AssemblyCenter、Anchor_Spawn、Anchor_Portal 供后续玩法接线，当前没有车辆移动或传送逻辑。Goal 使用同款平台与红色标牌，传送门默认隐藏。完整 beautiful corner 中平台缩放为 0.6，模块库保持原尺寸。

关卡作者可以组合桥段、台阶和支架，按实际车宽缩放并添加自己的碰撞体。桥墩的 Deck 对接点对应桥面高度。

## 植被、岩石和装饰升级

`Prefabs/Botanical/`（相对于 Assets/CreekDiorama）包含 18 个独立模块：三种层状岩块、河石、碎石组、柔软草丛、蕨类、阔叶灌木、垂藤、苔藓边缘、雏菊、羽扇豆、分枝松树、圆冠树、灯笼、木桶、木箱和浮叶。植物叶片使用真实几何，不依赖透明裁剪贴图。

`ArtSource/CreekDiorama/CreekBotanicalKit.blend` 是可编辑源文件。`Tools/CreekDiorama/build_botanical_kit.py` 同时生成模型、石灰岩与木纹的无缝颜色/法线贴图。原始参考图没有作为贴图使用。纹理仅用于细小表面变化，岩石的大面、缺口和植物轮廓来自模型。

叶片材质使用 `Shaders/CreekFoliage.shader`，根据叶片正反面修正受光，并增加柔和背光，减少薄叶背面过黑的问题；支持 URP 阴影与立体渲染宏。模型本身没有风动画。

独立 BeautifulCorner 场景与 Prefab 使用新的分层岩壁、苔藓边缘、垂藤、花簇和分枝树冠；桥面与平台尺寸沿用原模块。`CreekBotanicalGallery.unity` 展示全部新模块。场景中 `10 Botanical dressing/Render batches` 为实际显示的合并网格；其旁边隐藏的 `Editable placement` 保留所有嵌套 Prefab 摆放。手工调整时先关闭 Render batches，再启用 Editable placement，避免重复显示。

Unity 菜单 **MetaHackthone > Creek Art > Build botanical dressing** 可从已生成 FBX 重新导入并摆放，运行前保存打开的场景。此操作覆盖本套生成的摆放；手动摆放应先另存自己的场景。基础模块重新生成时，也会保留并重新应用已安装的植物岩石套件。

最新画面：`BeautifulCorner-Botanical.png`、`Botanical-WaterfallDetail.png`、`BotanicalGallery.png`。模型和场景检查结果记录在 `Botanical-Verification.txt`；新增装饰无碰撞体和玩法脚本，尚未做头显性能测试。

## 水体材质

下列路径均相对于 Assets/CreekDiorama/：

- Shaders/CreekWater.shader、Materials/Living river.mat：流动波纹、深浅水色、移动焦散、细碎反光与岸边泡沫。
- Materials/Modular river.mat：开启 World Space，使用世界坐标计算流动和波浪，相邻水面块保持连续；同一水面保持水平和相同高度。
- Shaders/CreekWaterfall.shader、Materials/Silk waterfall.mat：向下流动的水丝与白沫。
- Shaders/CreekImpact.shader、Materials/Waterfall impact.mat：扩散落水涟漪。
- Shaders/CreekSpray.shader、Materials/Waterfall spray.mat：落水飞溅水珠。
- Shaders/CreekPortal.shader、Materials/Portal cyan.mat：可选的程序化传送门效果。

Shader 使用内置时间，不需要行为脚本；Blender 保留静态建模材质。单独瀑布模块只包含流水网格，落水泡沫和飞溅可从完整样板的水体分组中复用、按 Impact 点放置。

URP 相机需要开启 **Depth Texture** 和 **Opaque Texture**，以启用接触泡沫与轻微透底；展示场景已配置。若不能启用，将水材质的 **Camera depth and opaque textures enabled** 设为 0，仍保留基础水色、波纹和反光。

早期模块展示截图临时启用了阴影以便观察体积，捕获后恢复原管线。项目原有阴影距离为 0，因此直接打开场景时投影可能与这些截图不同。本次云层与拼装台交付的 PC HDR 管线配置见下文。材质动画曾在 Play 模式连续帧中验证，尚未做头显设备验证。

## 重新生成

1. Blender 后台运行 Tools/CreekDiorama/build_modular_kit.py，生成模块 .blend、FBX 和 module_anchors.json。
2. Unity 菜单 **MetaHackthone > Creek Art > Build independent beautiful corner and modules** 生成原生模块网格、20 个 Prefab、独立样板与陈列场景。先保存打开的场景。
3. 批处理入口 CreekDiorama.Editor.CreekArtKitAuthoring.BuildAndCapture 生成并输出截图，结束时退出 Editor。

这些命令会覆盖本套生成资产，手工调整前请复制为自己的文件。原始完整沙盘仍使用 build_creek.py 与 CreekEnvironmentImporter 工作流；无需为摆放模块重新生成原始沙盘。

模型和配色由本项目脚本生成。云的主形状纹理由内置 imagegen 生成，提示词见 CloudTexture-Prompt.txt；外围噪声使用用户提供的 Luos Noise V2 纹理包中的 T_Lu_Noise_12 与 T_Lu_Noise_01，已复制到项目的 Textures/Clouds 下。

## 验证与现有项目状态

2026-11-02：Unity 6000.3.21f1 中生成 20 个模块，检查无 Collider、无运行时行为、无缺失材质、无玩法资产依赖；确认标准直桥宽 0.40 m、FBX 坐标正确、拼车平台为起点平台变体。记录见 ArtKit-Verification.txt。

2026-11-03：上传前更新到 Plastic /main cs:57。队友的 DownhillSandbox 关卡保留该版本原样，美术只在独立场景中展示。旧关卡接入脚本、旧路线网格和实验截图不包含在这次交付中。

2026-11-05：上传前更新到 Plastic /main cs:68。本次交付包含上述美术目录，以及 QualitySettings 的 PC 渲染管线引用，以启用独立 BeautifulCornerPreviewPipeline 的 HDR 辉光；Mobile 配置不变。

## 写实云层和拼装台

- BeautifulCorner 场景及同名 Prefab 已包含分层 Billboard 云、透明边界、网格台面和方向标识。
- 云的主形状使用 RealisticCumulus.png，外围由 CloudPerimeterNoise.png 和 CloudWispNoise.png 控制扰动与薄雾。可在云材质中调整 Edge turbulence、Peripheral vapor 和 Noise drift。
- AssemblyBounds.prefab 是复用入口。修改根对象 Scale XYZ 可同时调整透明框和网格台面；不要单独缩放台面子对象。台面与边界使用同一单位空间范围。
- 中央箭头和圆环缩为上一版的 52%；在 Assembly tabletop 材质中调整 Direction emblem scale。
- 发光预览需要 HDR、相机 Post Processing 和 Assembly bloom VolumeProfile。PC 质量档已指向专用预览管线；移动端实际辉光与性能仍需实机验证。
- 最终预览见 BeautifulCorner-CloudNoise.png。已在三种长宽高下检查台面与边框的 XZ 范围一致，相关 Shader 编译无错误。

美术更新入口为 CreekDiorama.Editor.CreekCloudNoiseAuthoring.Apply；重新生成完整氛围可调用 CreekAtmosphereV2.Apply。调用会保存场景及 Prefab，手工摆放后应先备份。

## 网格云（Level 1–4）

原来的云是始终对齐视图矩阵的面片，在 VR 里会跟着头部转动和倾斜。先后试过光线步进体积云（细节不足、有抖动噪点，已删除），现在使用固定在世界中的网格云：实体云身加一层柔和雾壳。

- 网格：`Meshes/Effects/CloudPuff A..D.asset`，由编辑器脚本生成（细分二十面体贴到若干随机球的并集表面，底部压平，2,562 顶点）。顶点色 R = 球团相位，G = 底部遮罩，B = 凹缝深度。两个子网格共用三角形，分别给云身和雾壳材质。
- 云身：`Shaders/CreekCloudPuff.shader`（`Creek Diorama/Cloud Puff`），不透明。顶点阶段采样 3D 噪声 `Textures/Clouds/CloudVolumeNoise.asset` 沿法线位移，云团会翻滚、鼓胀，整朵云缓慢漂移和摆动；法线由相邻位移点重建。着色为主方向光的两段柔和色阶、高度渐变、凹缝自阴影和逆光暖色边缘。
- 雾壳：`Shaders/CreekCloudShell.shader`（`Creek Diorama/Cloud Shell`），透明、预乘混合（透视画面上 alpha 正确）。同一网格沿法线外扩绘制两层，朝向观察者处较浓、轮廓处淡出，并乘以滚动噪声形成丝缕。无光线步进，无抖动。
- 共用动画：`Shaders/CreekCloudPuff.hlsl`。
- 材质：`Materials/Cloud puff 00..09.mat` 与 `Cloud shell 00..09.mat`。可调 `_Billow`、`_BillowScale`、`_RollSpeed`、`_Swell`、`_Drift`、`_Tint`、`_ShadowTint`、`_SunRim`，雾壳另有 `_ShellInner`、`_ShellOuter`、`_ShellOpacity`、`_WispScale`、`_WispSpeed`。云身和雾壳的动画参数必须保持一致。
- 菜单：`MetaHackthone/Creek Art/Convert clouds to mesh puffs` 会重新生成网格和材质，并直接修改 Level1–4 的场景和 `Generated/Campaign` 预制体（不会重新烘焙赛道），可重复执行；运行模式下只更新资源。`Restore billboard clouds` 恢复旧面片；旧的 `Cloud layer NN` 目前以未激活状态保留。
- 已知限制：雾壳是基于视角的近似，贴得很近或相机进入云内时不成立。每朵云绘制三次（云身 + 两层雾壳）。
- 性能：尚未在 Quest 3 上测量。

## 渲染 A/B 测试（Quest 开发版）

`Assets/CloudGarden/Integration/QuestRenderAB.cs` 只编入 Development Build。运行：

```powershell
Tools\Test-QuestRenderAB.ps1 -Serial <序列号> -Auto
```

`-Auto` 会自动进入沙盒并把视角转向工作台；不加则需戴上头显手动进入并保持注视工作台。脚本通过 Meta VR CLI 启动应用、读取日志并截图，结果写入 `Logs/RenderAB/`。测试只开关 Renderer、注视点渲染和 MSAA，结束后恢复。

2026-11-11 Level 1 结果（Quest 3，头显静置、仰角约 30°，眼缓冲 2688x2816、4x MSAA、无注视点渲染，两轮一致）：

| 变体 | FPS |
| --- | --- |
| 基线 | 68 |
| 关闭云雾壳 | 70 |
| 关闭云 | 72 |
| 关闭建造边界 | 68 |
| 关闭其他透明效果 | 69 |
| 关闭不透明场景 | 90 |
| 全部关闭 | 90（GPU 约 8 ms） |
| 注视点渲染最高 | 74 |
| MSAA 2x | 74 |

结论：边界不是原因，云约占 1 ms；空画面已占 11.1 ms 预算中的约 8 ms，其余主要是不透明场景。运行时修改眼缓冲倍率未生效，该项尚未测量。

### 性能设置（2026-11-11 启用）

一次性启用，未逐项测量：

- OpenXR Android 设置：Symmetric Projection、Optimize Buffer Discards、Multiview Render Regions（All Passes）。
- `OculusProjectConfig`：Processor Favor = GPU。
- `XRRenderQuality.cs`：注视点渲染最高档 + 动态注视点，眼图层 Normal 锐化。

同一 A/B 测试复测：全部变体 90 FPS（之前基线 68）。帧率由动态分辨率维持，基线视口比例约 0.57（眼缓冲 3264x2816），画面清晰度低于之前；头显位置与上次不完全相同。刷新率仍为 90 Hz；Application SpaceWarp 未启用。

### Application SpaceWarp（已试用并放弃，2026-11-12）

试过 90 Hz（应用 45 帧）和 120 Hz（应用 60 帧）。应用帧率减半后手部和抓取的响应变差，影响玩法，因此关闭：OpenXR Android 功能 `Meta XR Space Warp` 已停用，为它添加的自定义着色器运动矢量通道已删除。当前为 90 Hz、每帧都渲染（`XRRenderQuality.DisplayFrequency`）。`-SpaceWarp` 对比模式仍留在测试脚本中，但需要重新启用该功能才有意义。下表为当时的测量记录。

### SpaceWarp 对比（2026-11-12，佩戴头显注视建造区）

`Tools\Test-QuestRenderAB.ps1 -Serial <序列号> -SpaceWarp`（不加 `-Auto`，佩戴后手动进入 Level 1）。每个变体稳定 5 秒、采样 6 秒。

| 变体 | 应用 FPS | 6 秒内丢帧 | 渲染分辨率（相对头显默认） | GPU 占用 |
| --- | --- | --- | --- | --- |
| SpaceWarp 开 | 45（显示 90） | 0 | 约 1.15–1.2x | 94–95% |
| SpaceWarp 开，上限 1.2x | 45 | 0 | 约 1.1x | 95% |
| SpaceWarp 开，上限 1.0x | 45 | 0 | 1.0x | 91% |
| SpaceWarp 关 | 82–86 | 33–75 | 0.7x（下限） | 97% |

静止注视时分辨率上限没有可测差别；一次大幅转头（约 138°）的采样出现 33 次丢帧，掉帧更可能发生在转头过程中，尚未单独测量。

## 沙盒界面调整（2026-11-12）

- 建造边界：`SandboxBuildVolume.ShowWalls` 默认关闭，只画工作台面；边界仍限制零件位置。勾选后恢复发光墙和霓虹框。
- 旋转盘：`Steering rim` 改为发光圆环加中心箭头（`Resources/SteeringEmblem.shader`、`SteeringEmblem.mat`、`SteeringEmblemDisc.asset`），三根辐条已删除，工作台面上不再画圆环和箭头。抓取范围不变：在半径 0.17 m 的环附近捏合并绕中心移动。
- 文字：沙盒关卡不再显示状态/提示文字和手掌菜单的操作说明；`Session.Status` 仍在记录。倒计时、按钮和关卡标题保留。
- 以上改动只在编辑器中确认了数据并通过了 Android 构建，头显中的效果由使用者目测确认；未运行自动化测试。

## 关卡布局、河面深度和着色器抗锯齿（2026-11-12）

- 关卡布局：Level 1 场景中 `Building Area` 相对 `Sandbox Level` 的位姿（位置 (-3.0955, 2.2180, 5.9007)，偏航 152.28°）已套用到 Level 2–5 场景和 `Generated/Campaign/Level1–5.prefab`。
- 河面：`Settings/Mobile_RPAsset.asset` 打开了 Depth Texture，头显中的 `Living river` 才有按水深变化的颜色和岸边泡沫（编辑器 Scene 视图一直有深度）。Opaque Texture 仍关闭。`ProjectBuilder.ConfigureRendering` 同步改为保留深度纹理。每帧多一次深度处理，对帧率的影响尚未测量。
- 抗锯齿：`SteeringEmblem.shader` 的箭头和虚线环缺口改用 `fwidth` 得到约一像素宽的过渡；`CreekPortal.shader` 的圆形裁切改为 `fwidth` 覆盖率加 `AlphaToMask On`；`CreekWater.shader` 的细泡沫噪声在一个像素覆盖约一个噪声格时淡出为平均值。
- 验证：三个着色器编译无错误，旋转盘在编辑器中于 0.45 m、1.5 m、4 m 做了前后静态对比；传送门和河面未截图对比，运动中和头显中的效果未验证。

## 头显渲染设置实测与关卡布局统一（2026-11-12）

在 Quest 3 上用开发版的实时切换钩子（`QuestRenderAB` 的 live 模式，`Tools/Test-QuestRenderAB.ps1 -Live -Auto`）测量。条件：沙盒关卡、朝向工作台、头显静置未佩戴、每项约 10 秒，只覆盖这一个视角。

| 设置 | 分辨率 | MSAA | fps | GPU ms |
|---|---|---|---|---|
| 动态分辨率 + 深度纹理（当天早些时候的构建） | 0.70x | 4x | 60 | 13.6 |
| 动态分辨率，深度纹理关 | 0.70x | 4x | 87–89 | 8.4–9.0 |
| 固定 0.8x | 0.80x | 4x | 88 | 8.8 |
| 固定 1.0x | 1.00x | 4x | 75 | 10.1 |
| 固定 1.0x（现用） | 1.00x | 2x | 87 | 8.7 |
| 固定 1.1x | 1.10x | 2x | 84 | 8.9 |
| 固定 1.2x | 1.20x | 2x | 71 | 10.4 |

- 深度纹理每帧约多 5 ms，已重新关闭（`Mobile_RPAsset.asset`、`ProjectBuilder.ConfigureRendering`）；`Living river.mat`、`Modular river.mat` 的 `_UseDepth` 设为 0，编辑器和头显的水面一致。上一节"河面"条目中打开深度纹理的做法已撤销。
- 动态分辨率原先分配 1.6x 的缓冲并长期停在下限（等效 0.70x）。现在关闭（`MetaHackathonPlayer.prefab`、`Workshop.unity`、`XRRenderQuality.Apply`），固定 1.0x，MSAA 改为 2x。负载更高的场景不再降分辨率，而是掉帧。
- 注视点渲染通过 Meta SDK（`OVRManager.foveatedRenderingLevel`，High）生效，约省 1 ms。`XRDisplaySubsystem.foveatedRenderingLevel` 在本项目无效（Unity 的 Foveated Rendering OpenXR 功能未启用）。锐化对耗时没有可测影响。
- 新默认值装机后复测：1.00x、MSAA 2x、约 87 fps、GPU 约 8.7 ms。画面观感（锯齿是否改善）尚未由人佩戴确认；转头和其他关卡未测。
- 关卡布局：Level 2–4 的环境美术与 Level 1 相同，但原先旋转了 75–105° 并有位移，赛道跟随其摆放。现在把环境和赛道作为整体移到 Level 1 的摆放（环境在 `Sandbox Level` 内位置 (0, 0.65, 2)、偏航 0），各赛道在环境中的相对位置不变。Level 2–5 的 `Player Start` 改为 Level 1 的值 (0, 0.945, -0.664)。Level 5 没有环境美术且缩放不同，赛道未动。已应用到 Level 2–5 场景和 `Generated/Campaign` 预制体；布局与工厂校验通过，EditMode 布局测试 10/10 通过，头显中未目测。
