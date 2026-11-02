using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Agamemnon.Drawing;

// 接口验证示例：只用包的公开接口画一个半径 1 mm 的圆，并渲染 PNG。
// 路径一：IDesignDocument 创建/更新/撤销；路径二：ISvgImporter 导入 SVG 圆，两条路径互相印证。
internal static class Program
{
    private static void Main(string[] args)
    {
        // ---- 路径一：IDesignDocument（文档编辑接口）----
        IDesignDocument doc = new DesignDocument();
        doc.Create(DesignObjectKind.Ellipse);
        Guid id = doc.Selected.Id;
        doc.Update(doc.Selected with { Name = "圆 r=1mm", WidthMm = 2, HeightMm = 2, Resolution = 180 });
        DesignObject circle = doc.Find(id);
        var paths = DesignGeometry.Generate(circle);
        var bounds = DesignGeometry.Bounds(paths);
        double radiusError = paths[0].Max(p => Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - 1.0));
        Console.WriteLine("[IDesignDocument] " + circle.Name + "：直径 " + circle.WidthMm + " mm，采样 " + paths[0].Count
            + " 点，包围盒 " + bounds.Width.ToString("F3") + " × " + bounds.Height.ToString("F3")
            + " mm，半径最大偏差 " + radiusError.ToString("F6") + " mm");
        doc.Move(id, 0.001, 0);
        doc.Undo();
        Console.WriteLine("[IDesignDocument] Move(0.001,0)+Undo 后位置 = (" + doc.Find(id).Position.X + ", " + doc.Find(id).Position.Y + ")，可撤销 " + doc.Snapshot.CanRedo);

        // ---- 路径二：ISvgImporter（SVG 导入接口）----
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"2mm\" height=\"2mm\" viewBox=\"0 0 2 2\"><circle cx=\"1\" cy=\"1\" r=\"1\"/></svg>";
        SvgImportResult imported = new SvgImporter().Parse(svg, "SVG 圆 r=1mm");
        var svgPaths = DesignGeometry.Generate(imported.Object);
        var svgBounds = DesignGeometry.Bounds(svgPaths);
        double svgRadiusError = svgPaths.SelectMany(p => p).Max(p => Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - 1.0));
        Console.WriteLine("[ISvgImporter] " + imported.Object.Name + "：" + svgPaths.Count + " 条轮廓共 "
            + svgPaths.Sum(p => p.Count) + " 点，包围盒 " + svgBounds.Width.ToString("F3") + " × " + svgBounds.Height.ToString("F3")
            + " mm，半径最大偏差 " + svgRadiusError.ToString("F6") + " mm");

        // ---- 渲染 PNG（设计系 mm → 像素）----
        string output = args.Length > 0 ? args[0] : "circle-r1mm.png";
        Render(output, paths, svgPaths);
        Console.WriteLine("[Render] 已写出 " + Path.GetFullPath(output));
    }

    private static void Render(string file, IReadOnlyList<IReadOnlyList<DesignPoint3>> blue,
        IReadOnlyList<IReadOnlyList<DesignPoint3>> red)
    {
        const double pxPerMm = 280;
        const int width = 900, height = 1030, top = 90;
        double ox = width / 2.0, oy = top + (height - top) / 2.0;
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);
            using (var titleFont = new Font("Microsoft Sans Serif", 16, FontStyle.Bold))
            using (var textFont = new Font("Microsoft Sans Serif", 11))
            {
                g.DrawString("Agamemnon.Drawing 半径 1 mm 圆（两条接口路径）", titleFont, Brushes.Black, 16, 14);
                g.DrawString("蓝实线：IDesignDocument 创建 Ellipse 直径 2 mm（180 段采样）", textFont, Brushes.SteelBlue, 20, 50);
                g.DrawString("红虚线：ISvgImporter 导入 <circle r=\"1mm\">（0.001 mm 自适应细分）", textFont, Brushes.Firebrick, 20, 70);
            }
            float MmX(double x) => (float)(ox + x * pxPerMm);
            float MmY(double y) => (float)(oy - y * pxPerMm);
            // 0.5 mm 网格
            using (var grid = new Pen(Color.Gainsboro, 1))
                for (double v = -3; v <= 3.001; v += 0.5)
                {
                    g.DrawLine(grid, MmX(v), top, MmX(v), height - 10);
                    g.DrawLine(grid, 10, MmY(v), width - 10, MmY(v));
                }
            // 坐标轴（设计系：原点左下、+Y 向上）
            using (var axis = new Pen(Color.Gray, 1.5f))
            {
                g.DrawLine(axis, MmX(-3), MmY(0), MmX(3), MmY(0));
                g.DrawLine(axis, MmX(0), MmY(-3), MmX(0), MmY(3));
            }
            void Draw(IReadOnlyList<IReadOnlyList<DesignPoint3>> pathList, Pen pen)
            {
                foreach (var path in pathList)
                {
                    var points = path.Select(p => new PointF(MmX(p.X), MmY(p.Y))).ToArray();
                    if (points.Length > 1) g.DrawLines(pen, points);
                }
            }
            using (var redPen = new Pen(Color.Firebrick, 2) { DashStyle = DashStyle.Dash })
                Draw(red, redPen);
            using (var bluePen = new Pen(Color.SteelBlue, 3))
                Draw(blue, bluePen);
            // 圆心、半径标注与 1 mm 比例尺
            g.FillEllipse(Brushes.Black, MmX(0) - 3, MmY(0) - 3, 6, 6);
            using (var radius = new Pen(Color.Black, 1.5f))
                g.DrawLine(radius, MmX(0), MmY(0), MmX(1), MmY(0));
            using (var font = new Font("Microsoft Sans Serif", 11, FontStyle.Bold))
            {
                g.DrawString("r = 1 mm", font, Brushes.Black, MmX(0.38f), MmY(0.12f));
                g.DrawLine(Pens.Black, MmX(1.9f), height - 34, MmX(2.9f), height - 34);
                g.DrawLine(Pens.Black, MmX(1.9f), height - 40, MmX(1.9f), height - 28);
                g.DrawLine(Pens.Black, MmX(2.9f), height - 40, MmX(2.9f), height - 28);
                g.DrawString("1 mm", font, Brushes.Black, MmX(2.15f), height - 62);
                g.DrawString("设计坐标系：原点在圆心，+X 右 / +Y 上，单位 mm", new Font("Microsoft Sans Serif", 10), Brushes.DimGray, 16, height - 28);
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
        bitmap.Save(file, ImageFormat.Png);
    }
}
