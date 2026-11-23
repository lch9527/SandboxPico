# 拼车五关场景美术

## 五关起终点承托复查

按真实赛道首尾端点检查整条路宽：左、中、右各取一条线，检查道路末端及向岛内延伸的位置。五关共 60 个采样位置，同时验证关卡场景和运行时 Prefab。仅地形、石材和草地网格算作岛面，水、云、桥面与树木不能代替承托；岛内可有小幅自然起伏。

| 关卡 | 本轮处理 |
| --- | --- |
| 第一关 | 起点保留；终点补落脚岛，并降低偏高的旧终点台地及其装饰，消除约 82 cm 的高度错位。只复制调整本关的美术网格，不改共享原件 |
| 第二关 | 终点保留；起点补台地，消除约 47 cm 的悬空 |
| 第三关 | 起点、终点各补完整岩台和草地，配修理物资、植被及补给 |
| 第四关 | 上次修正的两座岛体复检通过，保留 |
| 第五关 | 起终点均补货运岛体；陡坡与岛面交界处分段，使道路后方成为较平的出发平台 |

`CampaignLandingAuthoring.Apply` 检查并修正有问题的一端，正常端点保持原状；完整美术重建也包含检查。新增地形沿用现有草地、石材和道具，不添加碰撞或相机贴图依赖。赛道、机关、玩家起点及操作区数据保持不变。

验证：41/41 EditMode、3/3 PlayMode 通过，覆盖两份关卡数据的完整路宽承托、原地形与新增道具净空、场景/预制体一致及五关实际加载。真机性能未复测。[承托检查结果](Campaign-Landing-Validation.txt)。

更新后的全景：[第一关](Campaign-Landings-Level1.png) · [第二关](Campaign-Landings-Level2.png) · [第三关](Campaign-Landings-Level3.png) · [第四关](Campaign-Landings-Level4.png) · [第五关](Campaign-Landings-Level5.png)。

## 第四关：起终点岛体修正

第四关原底景与道路两端存在错位，尤其起点木桥悬在较低岛面上方。现按真实赛道首尾端点补齐起点采石台和终点落脚岛：草地台面顺着道路端部坡度衔接，岩壁延伸到溪谷底部，侧边补树木、花草、垂藤、旗帜及补给。与新起点台穿插的旧绞盘移除，由岛面上的绞盘替代。赛道、机关、碰撞、玩家位置和操作区不变。

岛体位于 `Car campaign scenery/Quarry endpoint island Start` 与 `Finish`，保持锚定道路两端，不参与自动横向移位。倾斜地表用真实三角形检查道路净空，树木和动态道具仍按保守包围盒检查；没有给美术岛体添加碰撞体。`QuarryIslandAuthoring.Apply` 只更新第四关，完整美术重建也包含此步骤。

验证：31/31 EditMode、3/3 PlayMode 通过，包含道路两端向下射线命中岛面、场景/预制体一致、五关道路净空与实际加载。真机性能未复测。[第四关全景](Level4-Island-Landings.png) · [玩家位置运行截图](Level4-Island-Player.png)。

## 天空与背景衔接（2026-11-13）

五关补充近景—中景—远景三层过渡：每关四组草花、补给或碎石组合；背景增加五组灰绿松林浮岛及七组更浅的灰蓝石灰岩浮岛。沿用原有木材、植物、石材模型，远景采用低面数松树，颜色逐层接近云影和天空。新增景物仍通过道路净空检查；不改变赛道、机关、玩家起点或操作区。

`Prefabs/CarScenery/Transitions/` 下有五组组合：Meadow verge、Mooring supplies、Workshop supplies、Quarry scree、Freight garden。普通版本每组一个 Renderer，`Editable` 版本保留可单独摆放的植物、石块、箱桶和灯笼实例。原有 37 个拆分道具保持可用。

每关新增背景共 15,888 个三角形、2 个不透明 Renderer；四组近景合计 4 个 Renderer，最高 23,936 个三角形。新几何不投影，不增加纹理采样、相机深度、场景颜色、透明雾层或后处理。调色与静态明暗烘焙到顶点色，运行时不逐帧计算；现有云、风车与旗帜继续运动。上述是资产预算，不是头显 GPU 性能测量。

桌面游戏使用同色系渐变天空：通过 `CarScenerySky` 绑定到桌面摄影机的 Skybox 组件，退出场景时恢复原设置，不修改全局 RenderSettings。Android/XR 分支不安装此天空，保留现实透视背景；浮岛和云作为场景里的实际几何衔接。截图中的全屏天空仅代表桌面效果。

重建入口仍为 `Apply car campaign scenery`，内部调用 `CarSceneryBackdropAuthoring`。会替换自动美术分组及生成资源，手工编辑请使用独立实例或 Variant。

验证：58/58 EditMode、3/3 PlayMode 通过，检查五关实际加载、场景/Prefab 一致、道路净空、新 shader、动态道具以及桌面天空退出后的恢复。未重新构建 APK 或测试头显。

实际运行截图：[第二关](Car-Campaign-Backdrop-Level2.png)、[第四关](Car-Campaign-Backdrop-Level4.png)、[第五关](Car-Campaign-Backdrop-Level5.png)。均为桌面玩家位置的临时相机，关闭后处理与深度/颜色纹理；正式桌面摄影机使用同一天空材质。

已合并 Plastic `/main` cs167 的五关拼车玩法与渲染设置，延续 Creek Diorama 的木材、铜件、石灰岩、溪水和植物风格。以程序最新的玩家起点、操作区、环境和赛道摆放为基础，重新整合每关道具并检查道路净空。

| 关卡 | 新增道具与氛围 |
| --- | --- |
| 1 · Beautiful Corner | 木制休憩长椅、路线图、补给箱、风向小旗、野花 |
| 2 · Crab Bridge | 系泊木台、绳圈、蟹笼、救生圈和河岸蕨类 |
| 3 · Wheel Size Challenge | 轮胎修理台、大小轮展示架、工具箱和扳手 |
| 4 · Drill Drop Bridge | 木制绞盘、缆绳卷筒、手摇曲柄和切割石块 |
| 5 · Factory Final Run | 分层岩柱、溪水底座、货运风车、木箱、松树、垂藤和远景云 |

美术保存于各关 `Environment/Car campaign scenery`；场景与 `Generated/Campaign/Level1..5.prefab` 同步。新道具没有 Collider 或 Rigidbody。美术生成时逐一比较原有碰撞体、赛道数据、机关、起点、玩家位置和拼装区，若变化则中止保存。第五关原本为空的环境组现在有独立的景观，赛道曲线和机关布局沿用原版。

云保留现有实体网格与柔雾外壳，提高缓慢水平漂移、形状翻滚和雾丝流速。远近层使用不同幅度，整体沿同一风向错相运动；位移有界，不存在循环位置跳变。两个材质共享动画参数，网格剔除边界同步扩大。小旗轻摆，风车缓慢旋转；均为纯视觉行为。

## 编辑入口

- 手工摆放新增了 37 个独立零件、六套可展开组合和完整溪谷编辑版；目录、轴心及重建注意事项见 [手工素材与 shader 抗锯齿说明](Manual-Scenery-and-Shader-AA.md)。
- `Assets/CreekDiorama/Prefabs/CarScenery/`：六个可复用 Prefab，静态部分按材质合并网格，活动部件单独保留。
- `ArtSource/CreekDiorama/CarSceneryProps.blend`：Blender 4.3 可编辑模型源文件。
- `Tools/CreekDiorama/build_car_props.py`：生成原始几何和 FBX。
- Unity 菜单 `MetaHackthone > Creek Art > Apply car campaign scenery`：重新生成道具并同时更新五关场景、游戏预制体；会替换自身的 `Car campaign scenery` 分组。手工修改此分组前请复制保存。
- `PhysicalAssembly.Editor.CarSceneryReview.Review`：输出五关 Unity 渲染预览到 `Logs/CarScenery/`。预览使用临时摄影机，不保存摄影机到关卡。

本次使用当前机器已有的 PICO SDK 完成桌面检查；未增加软件包。头显帧率、透视显示和手部交互舒适度仍需实机验证。

## 验证（同步 cs160 时的历史记录）

最新 cs167 合并验证：56/56 EditMode 和 2/2 PlayMode 通过。覆盖五关实际加载、道具运动、净空、场景与游戏预制体装饰位姿一致，以及无相机深度/颜色纹理的水面配置。轻量范围框沿用程序最新操作区位置。此次没有重新构建 Android 或测试头显。

最新运行截图：[第二关](Car-Campaign-Merged-Level2.png)、[第五关](Car-Campaign-Merged-Level5.png)（桌面临时相机位于玩家起点，关闭后处理、相机深度/颜色纹理；不是头显双眼截图）。下列为较早 cs160 的验证记录：

- EditMode：18/18 通过，涵盖五关场景与游戏预制体一致、纯视觉道具、云身与雾壳参数、网格边界和玩家起点。
- PlayMode：2/2 通过，实际加载五个游戏关卡，验证道具运动及禁用后恢复姿态，捕获玩家位置的连续画面。
- 作者工具记录五关场景及预制体的原始玩法快照未变。保留 cs160 的 ShowWalls、方向盘外观、界面与 Quest 配置。
- 预览：[第二关玩家视角](Car-Campaign-Player-Level2.png)、[第三关](Car-Campaign-Level3.png)、[第五关](Car-Campaign-Level5.png)。完整运行截图与测试 XML 位于本地 Logs/CarScenery。
- 本机通过仓库外的 PICO SDK 目录链接使用已有 SDK，manifest 与 package lock 保持仓库原样。
