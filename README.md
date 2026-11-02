# DLW_Drawing（Agamemnon 画图功能包）

Agamemnon「Function 功能包分层」的第一个包：**画图域**的接口 + 实现。从主项目 `Function/Agamemnon.Drawing` 于 2026-11-02 抽出为独立仓库；主项目当前仍使用其内部 `Application/Design` 链路，本包为同一语义的独立演进线。

## 定位

只负责「设计图形数据 + 编辑 + 导入」，明确不做：像素模式映射与加工准备（工艺流程层）、2D 画布显示与 3D 渲染（显示层）、项目文件持久化、任何设备操作。net8.0 纯 BCL 依赖，无 UI/设备/厂商引用（有回归检查强制该隔离）。

## 目录

```text
src/Agamemnon.Drawing/
├── Model/       DesignPoint2/3、DesignBounds、九宫格锚点、DesignObject（7 种图形）、
│                DesignProcessingAssignment（预设引用）、DesignProjectSettings + DesignMapping
├── Geometry/    DesignGeometry：参数化图形→世界折线（mm）、Bounds、Validate
├── Editing/     IDesignDocument + DesignDocument（增删改/移动/缩放/分组/命中/撤销重做，100 步历史）
│                DesignDragService（定平面拖拽：Begin/Preview/Commit）
├── Import/      ISvgImporter + SvgImporter、IPath3DImporter + Path3DImporter（内部 SvgPathParser）
└── docs/        契约与边界说明（contract.md）
tests/           独立无硬件检查（54 项，与主仓库同一检查集）
```

## 公开接口速览

```csharp
IDesignDocument doc = new DesignDocument();
doc.Create(DesignObjectKind.Rectangle);          // 新建默认图形
doc.Move(id, 1.5, 0, 0);                          // 平移（mm）
doc.Resize(id, widthMm: 10);                      // 锁定比例缩放
doc.Place(id, "组A");                             // 分组/树序
doc.Undo(); doc.Redo();                           // 快照式历史（上限 100）
doc.HitTestXY(xMm, yMm, toleranceMm);             // XY 命中测试

ISvgImporter svg = new SvgImporter();
SvgImportResult r = svg.Parse(svgText, "名称");   // 矢量轮廓导入（≤5MB，≤50000 点）

IPath3DImporter xyz = new Path3DImporter();       // X,Y,Z(mm) 文本导入
```

## 单位与坐标系

- 长度全部 **mm**；设计系原点在**左下**、+X 向右、+Y 向上；`AngleDegrees` 为 XY 顺时针角。
- 坐标范围 ±100000 mm；单图形 ≤50000 点；文档 ≤200 对象、合计 ≤200000 点。
- SVG 的 y 向下自动翻转为设计 +Y；无单位按 96 px/in；曲线细分目标 0.001 mm。
- 详见 [docs/contract.md](docs/contract.md)。

## 构建与检查

```powershell
dotnet build src\Agamemnon.Drawing\Agamemnon.Drawing.csproj -c Release
dotnet run --project tests\Agamemnon.Drawing.Checks.csproj -c Release   # 54 项检查
```

需要 .NET 8 SDK（任意平台，无 Windows 依赖）。

## 验证记录

- 2026-11-02：上游主仓库 Debug/Release 全量无硬件回归 969 项通过（其中本包 54 项）；本仓库独立运行同一检查集通过。
- 已知边界：预设值的目录校验留给工艺层（本包仅结构校验）；项目持久化/画布显示/3D 预览不在包内。
