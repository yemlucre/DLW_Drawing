using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Agamemnon.Drawing;

// Function/Agamemnon.Drawing 画图包的无硬件检查：包隔离、几何生成/校验、文档编辑与撤销、
// SVG/坐标导入、拖拽与坐标映射。全部为纯内存操作，不依赖 UI 或设备。
internal static class DrawingPackageChecks
{
    internal static void Run(Action<bool, string> check, string output)
    {
        PackageIsolation(check);
        GeometryChecks(check);
        DocumentChecks(check);
        ImportChecks(check);
        DragChecks(check);
        MappingChecks(check);
    }

    private static void PackageIsolation(Action<bool, string> check)
    {
        var assembly = typeof(DesignObject).Assembly;
        check(assembly.GetName().Name == "Agamemnon.Drawing", "drawing package assembly is Agamemnon.Drawing");
        var refs = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        check(!refs.Any(n => n == "System.Windows.Forms" || n == "PresentationFramework" || n == "WindowsBase" || (n != null && n.StartsWith("Agamemnon."))),
            "drawing package references no UI framework and no existing Agamemnon assembly");
        check(typeof(IDesignDocument).IsAssignableFrom(typeof(DesignDocument)), "DesignDocument implements IDesignDocument");
        check(typeof(ISvgImporter).IsAssignableFrom(typeof(SvgImporter)) && typeof(IPath3DImporter).IsAssignableFrom(typeof(Path3DImporter)),
            "importers implement their package interfaces");
    }

    private static void GeometryChecks(Action<bool, string> check)
    {
        var ellipse = new DesignObject { Kind = DesignObjectKind.Ellipse, WidthMm = 10, HeightMm = 6, Resolution = 36 };
        var paths = DesignGeometry.Generate(ellipse);
        check(paths.Count == 1 && paths[0].Count == 37, "ellipse generates resolution+1 samples");
        var b = DesignGeometry.Bounds(paths);
        check(Near(b.Width, 10) && Near(b.Height, 6), "ellipse bounds match width and height in mm");

        var sharp = new DesignObject { Kind = DesignObjectKind.Rectangle, WidthMm = 10, HeightMm = 4 };
        check(DesignGeometry.Generate(sharp)[0].Count == 5, "sharp rectangle is a five point contour");
        var rounded = sharp with { CornerPercent = 50, SubdivisionMm = 0.01 };
        check(DesignGeometry.Generate(rounded)[0].Count > 8, "rounded rectangle subdivides corners");

        var line = new DesignObject { Kind = DesignObjectKind.Line, Points = Array.AsReadOnly(new[] { new DesignPoint3(0, 0, 0), new DesignPoint3(10, 0, 0) }), AngleDegrees = 90 };
        var lp = DesignGeometry.Generate(line);
        check(Near(lp[0][1].X, 0) && Near(lp[0][1].Y, -10), "positive angle rotates clockwise in design coordinates");

        Rejects<ArgumentException>(() => DesignGeometry.Validate(new DesignObject { Kind = DesignObjectKind.Ellipse, WidthMm = -1, HeightMm = 1, Resolution = 6 }), check, "negative size rejected");
        Rejects<ArgumentException>(() => DesignGeometry.Validate(new DesignObject { Kind = DesignObjectKind.Spiral2D, InitialRadiusMm = 0.1, RadiusIncrementMm = -0.2, Turns = 2, Resolution = 6 }), check, "spiral with non-positive end radius rejected");
        Rejects<ArgumentException>(() => DesignGeometry.Validate(new DesignObject { Kind = DesignObjectKind.Line, Points = Array.AsReadOnly(new[] { new DesignPoint3(0, 0, 1), new DesignPoint3(5, 0, 0) }) }), check, "2D line with nonzero Z rejected");
        Rejects<ArgumentException>(() => DesignGeometry.Validate(new DesignObject { Kind = DesignObjectKind.Line, Points = Array.AsReadOnly(new[] { new DesignPoint3(1, 1, 0), new DesignPoint3(1, 1, 0) }) }), check, "coincident line endpoints rejected");
        Rejects<ArgumentException>(() => DesignGeometry.Bounds(Array.Empty<IReadOnlyList<DesignPoint3>>()), check, "empty bounds rejected");
    }

    private static void DocumentChecks(Action<bool, string> check)
    {
        var doc = new DesignDocument();
        int events = 0; doc.Changed += (_, _) => events++;
        doc.Create(DesignObjectKind.Rectangle);
        var id1 = doc.Selected.Id;
        check(doc.Snapshot.Objects.Count == 1 && doc.Selected.Name == "矩形 1" && events == 1, "create adds named object, selects it and raises Changed");

        doc.Move(id1, 1, 2);
        check(doc.Find(id1).Position == new DesignPoint3(1, 2, 0) && events == 2, "move updates position and raises Changed");
        doc.Resize(id1, 10);
        check(Near(doc.Find(id1).WidthMm, 10) && Near(doc.Find(id1).HeightMm, 10), "locked aspect resize scales height with width");

        doc.Undo(); doc.Undo(); doc.Undo();
        check(doc.Snapshot.Objects.Count == 0 && !doc.Snapshot.CanUndo && events == 6, "three undos empty the document and exhaust history");
        doc.Redo();
        check(doc.Snapshot.Objects.Count == 1 && Near(doc.Find(id1).WidthMm, 5) && doc.Find(id1).Position == default && events == 7, "redo restores the creation state");

        long revision = doc.ContentRevision;
        Rejects<ArgumentException>(() => doc.Update(doc.Find(id1) with { WidthMm = -3 }), check, "invalid update rejected");
        check(doc.ContentRevision == revision, "rejected edit leaves content revision unchanged");

        var id2 = doc.Add(new DesignObject { Kind = DesignObjectKind.Rectangle, Name = "B", WidthMm = 5, HeightMm = 5, Position = new(0.1, 0, 0) }).Snapshot.SelectedId;
        check(doc.HitTestXY(0, 0, 0.5) == id2, "hit test returns topmost object");
        doc.Update(doc.Find(id2.Value) with { Visible = false });
        check(doc.HitTestXY(0, 0, 0.5) == id1, "hidden objects are skipped by hit test");
        doc.Update(doc.Find(id2.Value) with { Visible = true });
        check(doc.Select(id1).Changed && !doc.Select(id1).Changed, "selection change reported once and repeat is a no-op");
        doc.Select(id2);
        check(doc.DeleteSelected().Changed && doc.Snapshot.Objects.Count == 1 && doc.Snapshot.SelectedId == null,
            "delete selected removes the selected object");

        var grouped = new DesignDocument();
        Guid a = grouped.Create(DesignObjectKind.Rectangle).Snapshot.SelectedId.Value;
        Guid b = grouped.Create(DesignObjectKind.Rectangle).Snapshot.SelectedId.Value;
        Guid c = grouped.Create(DesignObjectKind.Rectangle).Snapshot.SelectedId.Value;
        Guid d = grouped.Create(DesignObjectKind.Rectangle).Snapshot.SelectedId.Value;
        grouped.Place(b, "G1");
        check(grouped.Snapshot.Objects.Select(o => o.GroupPath).SequenceEqual(new[] { "", "", "", "G1" }), "object appended to a new group keeps tree order");
        grouped.Place(a, "G1");
        check(grouped.Snapshot.Objects.Select(o => o.GroupPath).SequenceEqual(new[] { "", "", "G1", "G1" }), "second object appends inside the existing group");
        Rejects<ArgumentException>(() => grouped.PlaceGroup("G1", "G1"), check, "moving group into itself rejected");
        grouped.Shift(a, -1);
        check(grouped.Snapshot.Objects.Select(o => o.Id).SequenceEqual(new[] { c, d, a, b }), "shift reorders siblings inside the group");
        grouped.Place(d, "G2");
        grouped.PlaceGroup("G1", "G2");
        check(grouped.Snapshot.Objects.Select(o => o.GroupPath).SequenceEqual(new[] { "", "G2", "G2/G1", "G2/G1" }), "group nesting moves members and keeps tree order");

        var many = Enumerable.Range(0, DesignDocument.MaxObjects + 1)
            .Select(i => new DesignObject { Kind = DesignObjectKind.Rectangle, Name = "r" + i, WidthMm = 1, HeightMm = 1 }).ToArray();
        Rejects<ArgumentException>(() => grouped.ReplaceAll(many, new DesignProjectSettings()), check, "documents above 200 objects rejected");
        grouped.ReplaceAll(new[] { grouped.Find(a) }, new DesignProjectSettings());
        check(!grouped.Snapshot.CanUndo && !grouped.Snapshot.CanRedo && grouped.Snapshot.Objects.Count == 1, "replace all clears undo history");

        Rejects<ArgumentException>(() => doc.AssignProcessing(id1, new DesignProcessingAssignment { PresetId = "not-a-guid", PresetName = "x" }), check, "invalid preset id rejected");
        var values = new Dictionary<string, JsonElement>();
        using (var json = JsonDocument.Parse("{\"k\":1}")) values["k"] = json.RootElement.Clone();
        doc.AssignProcessing(id1, new DesignProcessingAssignment { PresetId = Guid.NewGuid().ToString(), PresetName = "预设A", Values = values });
        using (var json2 = JsonDocument.Parse("2")) values["extra"] = json2.RootElement.Clone();
        var stored = doc.Find(id1).Processing;
        check(stored.PresetName == "预设A" && stored.Values.Count == 1, "processing assignment stored with defensively copied values");
    }

    private static void ImportChecks(Action<bool, string> check)
    {
        var svg = new SvgImporter();
        const string sample = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 50\" width=\"50mm\" height=\"25mm\">" +
            "<g transform=\"translate(10,5)\"><rect x=\"0\" y=\"0\" width=\"20\" height=\"10\"/><circle cx=\"30\" cy=\"5\" r=\"5\"/>" +
            "<path d=\"M0 0 L5 5 Q10 0 15 5 Z\"/></g></svg>";
        var imported = svg.Parse(sample, "sample");
        check(imported.Object.Kind == DesignObjectKind.Svg && imported.Object.Paths.Count == 3, "svg import produces three contours");
        var b = DesignGeometry.Bounds(DesignGeometry.Generate(imported.Object));
        check(Near(b.Width, 17.5) && Near(b.Height, 5.0) && Near(b.Minimum.X, -8.75) && Near(b.Minimum.Y, -2.5),
            "svg geometry is centered on the design origin with expected mm extents");
        var raw = DesignGeometry.Bounds(imported.Object.Paths);
        check(Near(raw.Minimum.X, 5) && Near(raw.Maximum.X, 22.5) && Near(raw.Minimum.Y, -7.5),
            "svg translate offset and y-axis flip applied in mm before anchoring");
        Rejects<ArgumentException>(() => svg.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10mm\" height=\"10mm\"><text>x</text></svg>", "t"), check, "svg text must be converted to paths first");
        Rejects<ArgumentException>(() => svg.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100%\" height=\"10mm\"/>", "p"), check, "svg percentage size rejected");
        Rejects<ArgumentException>(() => svg.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10mm\" height=\"10mm\"><style/></svg>", "s"), check, "svg style element rejected");

        var paths = new Path3DImporter();
        var track = paths.Parse("X Y Z\n1 2 3\n4,5,6\n7\t8\t9", "track");
        check(track.Kind == DesignObjectKind.Path3D && track.Points.Count == 3, "coordinate import parses header and all separators");
        var tb = DesignGeometry.Bounds(DesignGeometry.Generate(track));
        check(Near(tb.Minimum.X, 1) && Near(tb.Maximum.Y, 8) && Near(tb.Depth, 6), "coordinate import bounds in mm");
        Rejects<ArgumentException>(() => paths.Parse("1 2", "bad"), check, "two-column coordinate row rejected");
    }

    private static void DragChecks(Action<bool, string> check)
    {
        var doc = new DesignDocument();
        doc.Create(DesignObjectKind.Rectangle);
        var id = doc.Selected.Id;
        var session = DesignDragService.Begin(doc, id, new(0, 0, 0), new(0, 0, 1), new(0, 0, 10), new(0, 0, -1));
        var delta = DesignDragService.Preview(session, new(3, -2, 10), new(0, 0, -1));
        check(delta == new DesignPoint3(3, -2, 0) && doc.Selected.Position == default, "drag preview is pure and keeps Z");
        DesignDragService.Commit(doc, session, delta.Value);
        check(doc.Selected.Position == new DesignPoint3(3, -2, 0), "drag commits one undoable move");
        doc.Undo();
        check(doc.Selected.Position == default && doc.Snapshot.CanRedo, "undo reverses the whole drag");
        check(DesignDragService.Preview(session, new(0, 0, 1), new(1, 0, 0)) == null, "parallel drag ray returns null instead of huge coordinates");
        session = DesignDragService.Begin(doc, id, default, new(0, 0, 1), new(0, 0, 10), new(0, 0, -1));
        doc.Create(DesignObjectKind.Rectangle);
        Rejects<InvalidOperationException>(() => DesignDragService.Commit(doc, session, new(1, 0, 0)), check, "stale drag rejected after document edit");
        Rejects<InvalidOperationException>(() => DesignDragService.Begin(doc, doc.Snapshot.Objects[^1].Id, default, new(0, 0, 1), new(0, 0, 10), new(1, 0, 0)), check, "drag plane parallel to view ray rejected");
    }

    private static void MappingChecks(Action<bool, string> check)
    {
        var map = new DesignCoordinateMapping { StageOriginMm = new(10, 20, 0) };
        check(DesignMapping.Transform(new(1, 2, 0), map) == new DesignPoint3(11, 22, 0), "identity mapping translates design point to stage origin");
        var rotated = DesignMapping.Transform(new(1, 0, 0), new DesignCoordinateMapping { StageOriginMm = new(10, 20, 0), RotationDegrees = 90 });
        check(Near(rotated.X, 10) && Near(rotated.Y, 19), "90 degree mapping rotates design axis onto stage axis");
        var flipped = DesignMapping.Transform(new(2, 0, 0), new DesignCoordinateMapping { StageOriginMm = new(10, 20, 0), XDirection = -1 });
        check(Near(flipped.X, 8) && Near(flipped.Y, 20), "negative direction flips the mapped axis");
        Rejects<ArgumentException>(() => DesignMapping.Validate(new DesignCoordinateMapping { XDirection = 2 }), check, "mapping direction must be plus or minus one");
        Rejects<ArgumentException>(() => new DesignDocument().UpdateSettings(new DesignProjectSettings { Mapping = new DesignCoordinateMapping { YDirection = 0 } }), check, "document settings reject invalid mapping");
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;

    private static void Rejects<TException>(Action action, Action<bool, string> check, string name) where TException : Exception
    {
        bool threw = false;
        try { action(); } catch (TException) { threw = true; } catch (Exception) { threw = false; }
        check(threw, name);
    }
}
