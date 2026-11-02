# Agamemnon.Drawing 画图功能包契约

检查日期：2026-11-02。能力 ID：`design.drawing_package`（implemented，agentExposed=false）。本文件是 `Function/Agamemnon.Drawing`（现独立仓库 DLW_Drawing）的接口契约与边界记录。

## 定位与边界

- **职责**：画图域（设计图形数据 + 编辑 + 导入）的自包含功能包，**接口 + 实现**都在包内。
- **不在包内**：像素模式映射与加工准备（属工艺流程层）、2D 画布显示与 3D 预览（属显示/渲染层）、项目文件持久化、任何设备操作。
- **依赖**：net8.0 纯 BCL（含 System.Text.Json）。不引用任何 UI 框架或设备程序集；有回归检查强制该隔离。
- **与上游的关系**：主项目 Agamemnon 现行有效实现仍为其 `src/Agamemnon.Application/Design` 链路（Desktop/MCP 使用）；本包是同一语义的独立演进线，切换/迁移需上游另行确认。

## 公开接口

| 接口/类型 | 实现 | 说明 |
|---|---|---|
| `IDesignDocument` | `DesignDocument` | 文档编辑入口：Create/Add/Update/Move/Resize/Place/PlaceGroup/Shift/DeleteSelected/Undo/Redo/UpdateSettings/AssignProcessing/ReplaceAll/HitTestXY/Select/Find；快照式撤销（上限 100 步），每次提交原子并触发 `Changed` |
| `ISvgImporter` | `SvgImporter` | SVG 文本/文件导入（5 MB 上限），只导矢量轮廓/中心线 |
| `IPath3DImporter` | `Path3DImporter` | X,Y,Z(mm) 坐标文本导入（5 MB 上限，#注释/表头可选） |
| `DesignGeometry`（静态） | — | 参数化图形→世界折线（mm）、Bounds、Validate；纯函数库故不设接口 |
| `DesignDragService`（静态） | — | 定平面拖拽：Begin/Preview（纯）/Commit（一次可撤销 Move） |
| `DesignMapping`（静态） | — | 设计↔位移台坐标纯换算与映射结构校验 |
| `DesignObject` 等 record | — | 不可变模型：7 种图形（Line/Ellipse/Rectangle/Spiral2D/Path3D/Helix3D/Svg）、九宫格锚点、加工预设引用（不透明） |

## 单位、坐标系与限制

- 长度全部 mm；设计系原点在左下、+X 右、+Y 上；`AngleDegrees` 为 XY 顺时针旋转（正角度把 +X 转向 -Y）。
- 坐标范围 ±100000 mm；单图形 ≤50000 点；文档 ≤200 对象、合计 ≤200000 生成点；历史 ≤100 步。
- 非法输入抛 `ArgumentException`（带中文原因），拖拽状态冲突抛 `InvalidOperationException`；无部分成功。
- `DesignProcessingAssignment.Values` 对本包不透明：仅做 PresetId(GUID)/PresetName(1–80) 结构校验并深拷贝；预设值目录校验不在本包（刻意边界，属工艺流程层）。
- SVG 限制：拒绝 style/script/动画/裁剪/use/文字/位图/百分比尺寸/嵌套 svg；preserveAspectRatio 仅 none 或九宫格 meet；无单位按 96 px/in；曲线细分目标 0.001 mm；SVG y 轴自动翻转为设计 +Y 向上；Svg 对象生成时按锚点 C 居中于原点。
- 命中测试自显示顺序顶层向下；`Preview` 在视线与平面平行时返回 null 而非大坐标；拖拽提交前校验 `ContentRevision` 防陈旧会话。

## 验证

- 2026-11-02：上游主仓库根 `test.ps1` Debug 与 Release 全量无硬件回归 **969 项通过**（本包 54 项，见 `tests/DrawingPackageChecks.cs`：包隔离、几何生成/校验、文档编辑/撤销/分组/命中、SVG 与坐标导入、拖拽、坐标映射）；正式/测试编译 0 错误。无设备操作。
- 本独立仓库：`dotnet run --project tests` 运行同一检查集（54 项）。
