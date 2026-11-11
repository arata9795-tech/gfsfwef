// Струбцина ЗУБР 32155-050-200_z01 (F-образная, 200 x 50 мм) — вариант 16.
// Строит 9 деталей, сборку с сопряжениями, чертежи и запускает демонстрацию зажима.
// SolidWorks 2025 + Visual Studio 2026, Console App (.NET Framework 4.8), x64,
// ссылки SolidWorks.Interop.sldworks / swconst, Embed Interop Types = False.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ZubrClamp
{
    internal static class Program
    {
        const double MM = 0.001;

        // Все детали строятся сразу в системе координат сборки (мм):
        // X — вдоль рейки, ось винта совпадает с осью X, рейка выше оси на 50 мм (глубина зажима).
        const double BarX0 = -16, BarX1 = 249, BarY0 = 50, BarY1 = 70, BarT = 6;
        const double P = 70;          // рабочая грань синей пяты в исходном положении (раскрытие 70 мм, как на фото)
        const double J = P + 30;      // передняя грань бобышки подвижной губки
        const double H0 = P + 67;     // начало рукоятки
        const double Pitch = 2;       // шаг резьбы винта

        // true — добавить деревянный брусок, чтобы было что зажимать (на фото его нет).
        static readonly bool AddWorkpiece = false;
        const double WorkpieceT = 40;
        static readonly bool MakeDrawings = true;

        static readonly double[] Steel = { 0.82, 0.84, 0.87 };
        static readonly double[] Black = { 0.06, 0.06, 0.07 };
        static readonly double[] Oxide = { 0.14, 0.14, 0.16 };
        static readonly double[] Blue = { 0.00, 0.26, 0.78 };
        static readonly double[] Wood = { 0.82, 0.63, 0.40 };

        static SldWorks sw;
        static MathUtility mu;
        static string outDir;

        [STAThread]
        static void Main()
        {
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("ОШИБКА: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
            Console.WriteLine();
            Console.WriteLine("Нажмите любую клавишу, чтобы закрыть окно...");
            Console.ReadKey(true);
        }

        static void Run()
        {
            try { sw = (SldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { sw = (SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application")); }
            sw.Visible = true;
            mu = (MathUtility)sw.GetMathUtility();

            // Новая папка на каждый запуск: иначе SaveAs упирается в файлы, открытые с прошлого запуска.
            outDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "Zubr_32155-050-200", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(outDir);
            Console.WriteLine("Папка проекта: " + outDir);

            var parts = new List<string[]>(); // {путь, имя, знаменатель масштаба чертежа}
            string rail = Step("01_Rail", BuildRail); parts.Add(new[] { rail, "01_Rail", "2" });
            string fixedJaw = Step("02_Fixed_Jaw", BuildFixedJaw); parts.Add(new[] { fixedJaw, "02_Fixed_Jaw", "1" });
            string jawPad = Step("03_Jaw_Pad (листовой металл)", BuildJawPad); parts.Add(new[] { jawPad, "03_Jaw_Pad", "1" });
            string movJaw = Step("04_Movable_Jaw", BuildMovableJaw); parts.Add(new[] { movJaw, "04_Movable_Jaw", "1" });
            string screw = Step("05_Screw", BuildScrew); parts.Add(new[] { screw, "05_Screw", "1" });
            string pad = Step("06_Swivel_Pad", BuildSwivelPad); parts.Add(new[] { pad, "06_Swivel_Pad", "1" });
            string collar = Step("07_Handle_Collar", BuildCollar); parts.Add(new[] { collar, "07_Handle_Collar", "1" });
            string grip = Step("08_Handle_Grip", BuildGrip); parts.Add(new[] { grip, "08_Handle_Grip", "1" });
            string cap = Step("09_Handle_End_Cap", BuildEndCap); parts.Add(new[] { cap, "09_Handle_End_Cap", "1" });
            string work = AddWorkpiece ? Step("10_Workpiece", BuildWorkpiece) : null;

            Console.WriteLine("Сборка...");
            var am = NewDoc(swUserPreferenceStringValue_e.swDefaultTemplateAssembly, "сборки");
            var assy = (AssemblyDoc)am;
            List<Feature> ap = Planes((Feature)am.FirstFeature());

            Component2 cRail = AddComp(assy, rail), cFixed = AddComp(assy, fixedJaw), cJawPad = AddComp(assy, jawPad);
            Component2 cMov = AddComp(assy, movJaw), cScrew = AddComp(assy, screw), cPad = AddComp(assy, pad);
            Component2 cCollar = AddComp(assy, collar), cGrip = AddComp(assy, grip), cCap = AddComp(assy, cap);
            Component2 cWork = work != null ? AddComp(assy, work) : null;

            // Неподвижные детали: три плоскости совпадают с плоскостями сборки.
            foreach (var c in new[] { cRail, cFixed, cJawPad, cWork })
            {
                if (c == null) continue;
                var cp = Planes((Feature)c.FirstFeature());
                for (int i = 0; i < 3; i++) Mate(am, assy, cp[i], ap[i], c.Name2 + " плоскость " + i);
            }
            // Подвижная губка: совпадают Спереди и Сверху, остаётся только скольжение вдоль рейки (X).
            var mp = Planes((Feature)cMov.FirstFeature());
            Mate(am, assy, mp[0], ap[0], "губка/Спереди");
            Mate(am, assy, mp[1], ap[1], "губка/Сверху");
            // Винт: соосен губке — может вращаться и перемещаться вдоль оси.
            Mate(am, assy, Axis(cScrew), Axis(cMov), "винт/ось губки");
            // Рукоятка (3 детали) жёстко на винте; пята соосна винту и прижата к его торцу, но вращается свободно.
            var sp = Planes((Feature)cScrew.FirstFeature());
            foreach (var c in new[] { cCollar, cGrip, cCap })
            {
                var cp = Planes((Feature)c.FirstFeature());
                for (int i = 0; i < 3; i++) Mate(am, assy, cp[i], sp[i], c.Name2 + "/винт " + i);
            }
            Mate(am, assy, Axis(cPad), Axis(cScrew), "пята/ось винта");
            Mate(am, assy, Planes((Feature)cPad.FirstFeature())[2], sp[2], "пята/торец винта");

            am.EditRebuild3();
            am.ShowNamedView2("*Isometric", (int)swStandardViews_e.swIsometricView);
            am.ViewZoomtofit2();
            string assyPath = Save(am, "00_Zubr_32155-050-200.SLDASM");
            Console.WriteLine("Сборка сохранена: " + assyPath);

            if (MakeDrawings)
            {
                Console.WriteLine("Чертежи (Лаб. 5–6)...");
                foreach (var p in parts) Drawing(p[0], p[1], double.Parse(p[2]), false);
                Drawing(assyPath, "00_Zubr_32155-050-200", 2, true);
                int err = 0;
                sw.ActivateDoc3(am.GetPathName(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref err);
            }

            Demo(am, cMov, new[] { cScrew, cCollar, cGrip, cCap }, cPad);
            am.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, 0, 0);
        }

        // ---------------------------------------------------------------- детали

        static ModelDoc2 BuildRail()
        {
            var m = NewPart();
            Sketch(m, 0);
            Rect(m, BarX0, BarY0, BarX1, BarY1);
            ExtrudeMid(m, BarT, "Рейка");

            // Продольные канавки и фаски — по всей длине рейки, как на закалённой рейке ЗУБР.
            Sketch(m, 2);
            double h = BarT / 2;
            foreach (double s in new[] { 1.0, -1.0 })
            {
                foreach (double y in new[] { 56.0, 64.0 })
                    Poly(m, s * (h + 0.5), y - 1.4, s * (h - 0.8), y, s * (h + 0.5), y + 1.4);
                Poly(m, s * (h - 0.8), BarY0 - 0.2, s * (h + 0.4), BarY0 - 0.2, s * (h + 0.4), BarY0 + 1.0);
                Poly(m, s * (h - 0.8), BarY1 + 0.2, s * (h + 0.4), BarY1 + 0.2, s * (h + 0.4), BarY1 - 1.0);
            }
            CutThroughBoth(m, "Канавки и фаски");

            Sketch(m, 0);
            Circle(m, BarX1 - 5, (BarY0 + BarY1) / 2, 2.5);
            ExtrudeMid(m, BarT + 1.4, "Упор губки");

            Finish(m, "AISI 1045 Steel, cold drawn", Steel, 0.9, 0.85);
            return m;
        }

        static ModelDoc2 BuildFixedJaw()
        {
            var m = NewPart();
            double[] outer = { -16, 76, 6, 76, 6, 46, -3, 36, -3, -14, -19, -14, -16, 46 };
            double[] pocket = { -6, 30, -6, -4, -15.5, -4, -13.8, 30 };

            Sketch(m, 0); Poly(m, outer);
            ExtrudeMid(m, 7, "Стенка");
            // Наружный контур + карман = двутавровое литьё с выборками с обеих сторон.
            Sketch(m, 0); Poly(m, outer); Poly(m, pocket);
            ExtrudeMid(m, 16, "Рёбра");
            Sketch(m, 0); Circle(m, -10, 60, 2.2); Circle(m, 0, 60, 2.2);
            ExtrudeMid(m, 17, "Заклёпки");
            Sketch(m, 2); Rect(m, -BarT / 2, BarY0, BarT / 2, BarY1);
            CutThroughBoth(m, "Паз под рейку");

            Finish(m, "Malleable Cast Iron", Black, 0.35, 0.3);
            return m;
        }

        // Синяя накладка неподвижной губки — U-образная деталь из листового металла (Лаб. 4).
        static ModelDoc2 BuildJawPad()
        {
            foreach (bool flip in new[] { false, true })
            {
                var m = NewPart();
                Sketch(m, 1);
                Line(m, -8, 8, -3, 8); Line(m, -3, 8, -3, -8); Line(m, -3, -8, -8, -8);
                EndGeom(m);
                var fm = m.FeatureManager;
                CustomBendAllowance cba = fm.CreateCustomBendAllowance();
                cba.Type = (int)swBendAllowanceTypes_e.swBendAllowanceKFactor;
                cba.KFactor = 0.45;
                var f = fm.InsertSheetMetalBaseFlange2(3 * MM, flip, 1 * MM, 24 * MM, 0, false,
                    (int)swEndConditions_e.swEndCondMidPlane, (int)swEndConditions_e.swEndCondBlind, 0,
                    cba, true, (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular,
                    0.5 * MM, 0.5 * MM, 0.5, true, true, true, true);
                // Толщина листа должна уйти наружу губки (до X = 0), иначе пересоздаём с обратным направлением.
                var box = f != null ? (double[])((PartDoc)m).GetPartBox(true) : null;
                if (box != null && box[3] > -1.5 * MM)
                {
                    f.Name = "Базовая кромка";
                    Finish(m, "ABS", Blue, 0.3, 0.4);
                    return m;
                }
                sw.QuitDoc(m.GetTitle());
            }

            Console.WriteLine("   (листовой металл не создался — накладка построена вытягиванием)");
            var s = NewPart();
            Sketch(s, 1);
            Poly(s, -8, 11, 0, 11, 0, -11, -8, -11, -8, -8, -3, -8, -3, 8, -8, 8);
            ExtrudeMid(s, 24, "Накладка");
            Finish(s, "ABS", Blue, 0.3, 0.4);
            return s;
        }

        static ModelDoc2 BuildMovableJaw()
        {
            var m = NewPart();
            double[] outer = { J - 4, 76, J + 18, 76, J + 18, 44, J + 22, 14, J + 22, -12, J, -12, J, 14, J - 4, 44 };
            double[] pocket = { J + 3, 36, J + 14, 36, J + 17, 16, J + 5, 16 };

            Sketch(m, 0); Poly(m, outer);
            ExtrudeMid(m, 7, "Стенка");
            Sketch(m, 0); Poly(m, outer); Poly(m, pocket);
            ExtrudeMid(m, 16, "Рёбра");
            Sketch(m, 2);
            Rect(m, -BarT / 2 - 0.15, BarY0 - 0.15, BarT / 2 + 0.15, BarY1 + 0.15);
            Circle(m, 0, 0, 5.1);
            CutThroughBoth(m, "Паз под рейку и резьбовое отверстие");
            XAxis(m);

            Finish(m, "Malleable Cast Iron", Black, 0.35, 0.3);
            return m;
        }

        static ModelDoc2 BuildScrew()
        {
            var m = NewPart();
            Sketch(m, 0);
            CenterLine(m, P - 5, H0 + 50);
            var pts = new List<double> { P + 4, 0, P + 4, 4, P + 9, 4 };
            for (double x = P + 9; x < H0 - 0.01; x += Pitch)
            {
                pts.Add(x + Pitch / 2); pts.Add(5);
                pts.Add(x + Pitch); pts.Add(4);
            }
            pts.AddRange(new double[] { H0 + 40, 4, H0 + 40, 0 });
            Poly(m, pts.ToArray());
            Revolve(m, "Винт с резьбой");
            XAxis(m);

            Finish(m, "AISI 1045 Steel, cold drawn", Oxide, 0.6, 0.5);
            return m;
        }

        static ModelDoc2 BuildSwivelPad()
        {
            var m = NewPart();
            Sketch(m, 0);
            CenterLine(m, P - 5, P + 15);
            Poly(m, P, 0, P, 10.5, P + 1.5, 12, P + 7, 12, P + 9, 10, P + 9, 4.2, P + 4, 4.2, P + 4, 0);
            Revolve(m, "Пята");
            XAxis(m);

            Finish(m, "ABS", Blue, 0.35, 0.45);
            return m;
        }

        static ModelDoc2 BuildCollar()
        {
            var m = NewPart();
            Sketch(m, 0);
            CenterLine(m, H0 - 5, H0 + 30);
            Poly(m, H0, 4, H0, 7, H0 + 3, 8.2, H0 + 15, 10.5, H0 + 20, 12.4, H0 + 22, 13, H0 + 22, 4);
            Revolve(m, "Воротник");

            Finish(m, "ABS", Black, 0.2, 0.2);
            return m;
        }

        static ModelDoc2 BuildGrip()
        {
            var m = NewPart();
            double a = H0 + 22, b = H0 + 108;
            Sketch(m, 0);
            CenterLine(m, a - 5, b + 10);
            Line(m, a, 4, a, 13);
            Spline(m, a, 13, H0 + 35, 14.0, H0 + 53, 14.6, H0 + 73, 14.1, H0 + 91, 13.2, H0 + 103, 13.4, b, 13.2);
            Line(m, b, 13.2, b, 0);
            Line(m, b, 0, H0 + 62, 0);
            Line(m, H0 + 62, 0, H0 + 62, 4);
            Line(m, H0 + 62, 4, a, 4);
            Revolve(m, "Корпус рукоятки");

            Sketch(m, 0);
            Rect(m, H0 + 45, 12.6, H0 + 100, 16);
            var groove = CutMid(m, 2.4, "Канавка");
            var axis = XAxis(m);

            // Круговой массив канавок (Лаб. 3: «применение массивов»).
            m.ClearSelection2(true);
            axis.Select2(false, 1);
            groove.Select2(true, 4);
            var pat = m.FeatureManager.FeatureCircularPattern4(8, 2 * Math.PI, false, "NULL", false, true, false);
            if (pat != null) pat.Name = "Массив канавок";
            else Console.WriteLine("   (круговой массив не создался — осталась одна канавка)");
            m.ClearSelection2(true);

            Finish(m, "ABS", Blue, 0.35, 0.45);
            return m;
        }

        static ModelDoc2 BuildEndCap()
        {
            var m = NewPart();
            double b = H0 + 108;
            Sketch(m, 0);
            CenterLine(m, b - 5, b + 12);
            Line(m, b, 0, b, 13.2);
            Spline(m, b, 13.2, b + 3.5, 12, b + 5.8, 8.6, b + 6.8, 4.2, b + 7, 0);
            Line(m, b + 7, 0, b, 0);
            Revolve(m, "Торцевой колпачок");

            Finish(m, "ABS", Black, 0.3, 0.3);
            return m;
        }

        static ModelDoc2 BuildWorkpiece()
        {
            var m = NewPart();
            Sketch(m, 0);
            Rect(m, 0, -30, WorkpieceT, 30);
            ExtrudeMid(m, 50, "Брусок");
            Finish(m, "Beech", Wood, 0.1, 0.1);
            return m;
        }

        // ---------------------------------------------------------------- демонстрация

        static void Demo(ModelDoc2 am, Component2 jaw, Component2[] rotating, Component2 pad)
        {
            double target = AddWorkpiece ? WorkpieceT : 0; // куда упирается пята
            double jawPos = 0, scr = 0, ang = 0;
            const double ScrMin = -15, ScrMax = 20, JawMin = -90, JawMax = 120;

            Action apply = () =>
            {
                jaw.Transform2 = Xf(0, jawPos);
                foreach (var c in rotating) c.Transform2 = Xf(ang, jawPos + scr);
                pad.Transform2 = Xf(0, jawPos + scr);
                am.GraphicsRedraw2();
            };
            Func<double> padFace = () => P + jawPos + scr;

            // Поворот рукоятки на угол da (рад): винт идёт на шаг за оборот, но не дальше упора.
            Func<double, bool> turn = da =>
            {
                double ns = scr + Pitch * da / (2 * Math.PI);
                if (ns < ScrMin || ns > ScrMax || P + jawPos + ns < target - 1e-6) return false;
                scr = ns; ang += da;
                return true;
            };
            Func<double, bool> slide = dx =>
            {
                double nj = jawPos + dx;
                if (nj < JawMin || nj > JawMax || P + nj + scr < target - 1e-6) return false;
                jawPos = nj;
                return true;
            };

            Action autoCycle = () =>
            {
                // 1) подводим губку почти вплотную, 2) докручиваем винт, 3) отжимаем и возвращаем.
                while (padFace() - target > 4 && slide(-Math.Min(1.5, padFace() - target - 4))) { apply(); Thread.Sleep(10); }
                while (turn(-0.25)) { apply(); Thread.Sleep(12); }
                if (padFace() - target > 0.01) { turn(-(padFace() - target) * 2 * Math.PI / Pitch); apply(); }
                Console.WriteLine("   Зажато.");
                Thread.Sleep(900);
                for (double done = 0; done < 3 * 2 * Math.PI && turn(0.25); done += 0.25) { apply(); Thread.Sleep(12); }
                while (jawPos < 0 && slide(Math.Min(1.5, -jawPos))) { apply(); Thread.Sleep(10); }
                while (Math.Abs(scr) > 0.01 && turn(-Math.Sign(scr) * Math.Min(0.25, Math.Abs(scr) * 2 * Math.PI / Pitch))) { apply(); Thread.Sleep(12); }
                Console.WriteLine("   Отжато.");
            };

            Console.WriteLine();
            Console.WriteLine("=== Демонстрация (окно консоли должно быть активным) ===");
            Console.WriteLine(" D / →  — крутить рукоятку по часовой: винт с пятой идёт к губке (зажим)");
            Console.WriteLine(" A / ←  — крутить против часовой (отжим)");
            Console.WriteLine(" W / ↑  — сдвинуть подвижную губку к неподвижной, S / ↓ — от неё");
            Console.WriteLine(" Enter  — автоматический цикл зажим/отжим");
            Console.WriteLine(" Esc    — выход (сборка сохранится в текущем положении)");
            autoCycle();

            while (true)
            {
                var k = Console.ReadKey(true).Key;
                bool moved = false;
                switch (k)
                {
                    case ConsoleKey.D: case ConsoleKey.RightArrow: moved = turn(-Math.PI / 6); break;
                    case ConsoleKey.A: case ConsoleKey.LeftArrow: moved = turn(Math.PI / 6); break;
                    case ConsoleKey.W: case ConsoleKey.UpArrow: moved = slide(-2); break;
                    case ConsoleKey.S: case ConsoleKey.DownArrow: moved = slide(2); break;
                    case ConsoleKey.Enter: autoCycle(); break;
                    case ConsoleKey.Escape: return;
                }
                if (moved) apply();
                Console.Write("\r   Раскрытие: {0,6:0.0} мм   поворот рукоятки: {1,7:0} град   ",
                    padFace() - target, -ang * 180 / Math.PI);
            }
        }

        // ---------------------------------------------------------------- помощники

        static string Step(string name, Func<ModelDoc2> build)
        {
            Console.WriteLine("Деталь " + name + "...");
            var m = build();
            m.EditRebuild3();
            m.ShowNamedView2("*Isometric", (int)swStandardViews_e.swIsometricView);
            m.ViewZoomtofit2();
            return Save(m, name.Split(' ')[0] + ".SLDPRT");
        }

        static ModelDoc2 NewDoc(swUserPreferenceStringValue_e pref, string what)
        {
            string tpl = sw.GetUserPreferenceStringValue((int)pref);
            var m = (ModelDoc2)sw.NewDocument(tpl, 0, 0, 0);
            if (m == null)
                throw new Exception("Не удалось создать документ " + what + " по шаблону «" + tpl +
                                    "». Проверьте Tools → Options → Default Templates.");
            return m;
        }

        static ModelDoc2 NewPart() { return NewDoc(swUserPreferenceStringValue_e.swDefaultTemplatePart, "детали"); }

        static string Save(ModelDoc2 m, string file)
        {
            string path = Path.Combine(outDir, file);
            int e = 0, w = 0;
            bool ok = m.Extension.SaveAs(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e, ref w);
            if (!ok) throw new Exception("Не удалось сохранить " + path + " (код " + e + ")");
            return path;
        }

        // Плоскости ищем по типу, а не по имени: так код работает и в русском, и в английском SolidWorks.
        // Порядок: 0 — Спереди (XY), 1 — Сверху (XZ), 2 — Справа (YZ).
        static List<Feature> Planes(Feature f)
        {
            var list = new List<Feature>();
            for (; f != null && list.Count < 3; f = (Feature)f.GetNextFeature())
                if (f.GetTypeName2() == "RefPlane") list.Add(f);
            if (list.Count < 3) throw new Exception("Не найдены стандартные плоскости");
            return list;
        }

        static Feature Axis(Component2 c)
        {
            for (var f = (Feature)c.FirstFeature(); f != null; f = (Feature)f.GetNextFeature())
                if (f.GetTypeName2() == "RefAxis") return f;
            throw new Exception("В " + c.Name2 + " нет оси");
        }

        // Ось вдоль X (пересечение плоскостей Спереди и Сверху) — ось винта.
        static Feature XAxis(ModelDoc2 m)
        {
            var p = Planes((Feature)m.FirstFeature());
            m.ClearSelection2(true);
            p[0].Select2(false, 0);
            p[1].Select2(true, 0);
            if (!m.InsertAxis2(true)) throw new Exception("Не удалось создать ось в " + m.GetTitle());
            m.ClearSelection2(true);
            Feature axis = null;
            for (var f = (Feature)m.FirstFeature(); f != null; f = (Feature)f.GetNextFeature())
                if (f.GetTypeName2() == "RefAxis") axis = f;
            axis.Name = "Ось винта";
            return axis;
        }

        static void Sketch(ModelDoc2 m, int plane)
        {
            m.ClearSelection2(true);
            Planes((Feature)m.FirstFeature())[plane].Select2(false, 0);
            m.SketchManager.InsertSketch(true);
            // Без привязок: иначе при мелком масштабе близкие точки «прилипают» друг к другу.
            m.SketchManager.AddToDB = true;
            m.SketchManager.DisplayWhenAdded = false;
        }

        static void EndGeom(ModelDoc2 m)
        {
            m.SketchManager.AddToDB = false;
            m.SketchManager.DisplayWhenAdded = true;
        }

        static void Line(ModelDoc2 m, double x1, double y1, double x2, double y2)
        {
            m.SketchManager.CreateLine(x1 * MM, y1 * MM, 0, x2 * MM, y2 * MM, 0);
        }

        static void CenterLine(ModelDoc2 m, double x1, double x2)
        {
            m.SketchManager.CreateCenterLine(x1 * MM, 0, 0, x2 * MM, 0, 0);
        }

        static void Poly(ModelDoc2 m, params double[] p)
        {
            int n = p.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Line(m, p[2 * i], p[2 * i + 1], p[2 * j], p[2 * j + 1]);
            }
        }

        static void Rect(ModelDoc2 m, double x1, double y1, double x2, double y2)
        {
            m.SketchManager.CreateCornerRectangle(x1 * MM, y1 * MM, 0, x2 * MM, y2 * MM, 0);
        }

        static void Circle(ModelDoc2 m, double x, double y, double r)
        {
            m.SketchManager.CreateCircleByRadius(x * MM, y * MM, 0, r * MM);
        }

        static void Spline(ModelDoc2 m, params double[] p)
        {
            var pts = new double[p.Length / 2 * 3];
            for (int i = 0; i < p.Length / 2; i++)
            {
                pts[3 * i] = p[2 * i] * MM;
                pts[3 * i + 1] = p[2 * i + 1] * MM;
            }
            m.SketchManager.CreateSpline2(pts, true);
        }

        static Feature Check(ModelDoc2 m, Feature f, string name)
        {
            if (f == null) throw new Exception("Не удалось создать элемент «" + name + "» в " + m.GetTitle());
            f.Name = name;
            m.ClearSelection2(true);
            return f;
        }

        static Feature ExtrudeMid(ModelDoc2 m, double depth, string name)
        {
            EndGeom(m);
            var f = m.FeatureManager.FeatureExtrusion2(true, false, false,
                (int)swEndConditions_e.swEndCondMidPlane, 0, depth * MM, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, (int)swStartConditions_e.swStartSketchPlane, 0, false);
            return Check(m, f, name);
        }

        static Feature CutMid(ModelDoc2 m, double depth, string name)
        {
            EndGeom(m);
            var f = m.FeatureManager.FeatureCut4(true, false, false,
                (int)swEndConditions_e.swEndCondMidPlane, 0, depth * MM, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                false, true, true, false, false, false,
                (int)swStartConditions_e.swStartSketchPlane, 0, false, false);
            return Check(m, f, name);
        }

        static Feature CutThroughBoth(ModelDoc2 m, string name)
        {
            EndGeom(m);
            var f = m.FeatureManager.FeatureCut4(false, false, false,
                (int)swEndConditions_e.swEndCondThroughAll, (int)swEndConditions_e.swEndCondThroughAll, 0, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                false, true, true, false, false, false,
                (int)swStartConditions_e.swStartSketchPlane, 0, false, false);
            return Check(m, f, name);
        }

        // Вращение вокруг единственной осевой линии эскиза.
        static Feature Revolve(ModelDoc2 m, string name)
        {
            EndGeom(m);
            var f = m.FeatureManager.FeatureRevolve2(true, true, false, false, false, false,
                (int)swEndConditions_e.swEndCondBlind, 0, 2 * Math.PI, 0,
                false, false, 0, 0, 0, 0, 0, true, true, true);
            return Check(m, f, name);
        }

        static void Finish(ModelDoc2 m, string material, double[] rgb, double specular, double shininess)
        {
            try { ((PartDoc)m).SetMaterialPropertyName2("", "SOLIDWORKS Materials", material); }
            catch (COMException) { }
            // Цвет задаём после материала, иначе его перекроет внешний вид материала.
            m.MaterialPropertyValues = new[] { rgb[0], rgb[1], rgb[2], 1.0, 1.0, specular, shininess, 0.0, 0.0 };
            m.ClearSelection2(true);
        }

        static Component2 AddComp(AssemblyDoc assy, string path)
        {
            var c = assy.AddComponent5(path, (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                "", false, "", 0, 0, 0);
            if (c == null) throw new Exception("Не удалось вставить в сборку " + path);
            if (c.IsFixed())
            {
                c.Select4(false, null, false);
                assy.UnfixComponent();
            }
            // AddComponent5 ставит в точку центр габарита, а детали уже построены в координатах сборки.
            c.Transform2 = Xf(0, 0);
            return c;
        }

        static void Mate(ModelDoc2 am, AssemblyDoc assy, Feature a, Feature b, string label)
        {
            am.ClearSelection2(true);
            a.Select2(false, 1);
            b.Select2(true, 1);
            int err = 0;
            var mate = assy.AddMate5((int)swMateType_e.swMateCOINCIDENT, (int)swMateAlign_e.swMateAlignCLOSEST,
                false, 0, 0, 0, 1, 1, 0, 0, 0, false, false, 0, out err);
            if (mate == null) Console.WriteLine("   Предупреждение: не создалось сопряжение " + label + " (код " + err + ")");
            am.ClearSelection2(true);
        }

        // Поворот вокруг оси винта (ось X) на angle и сдвиг вдоль X на dx мм.
        static MathTransform Xf(double angle, double dx)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            var a = new[] { 1, 0, 0, 0, c, s, 0, -s, c, dx * MM, 0, 0, 1, 0, 0, 0 };
            return (MathTransform)mu.CreateTransform(a);
        }

        static void Drawing(string modelPath, string name, double scaleDen, bool bom)
        {
            try
            {
                var dm = NewDocA3();
                var dd = (DrawingDoc)dm;
                ((Sheet)dd.GetCurrentSheet()).SetScale(1, scaleDen, true, true);
                // Первый угол проекций — как по ГОСТ 2.305.
                dd.Create1stAngleViews2(modelPath);
                var iso = dd.CreateDrawViewFromModelView3(modelPath, "*Isometric", 0.34, 0.08, 0);
                if (bom && iso != null)
                {
                    var model = (ModelDoc2)sw.GetOpenDocumentByName(modelPath);
                    string cfg = model != null ? model.ConfigurationManager.ActiveConfiguration.Name : "";
                    iso.InsertBomTable4(true, 0.405, 0.285,
                        (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopRight,
                        (int)swBomType_e.swBomType_PartsOnly, cfg, BomTemplate(), false,
                        (int)swNumberingType_e.swNumberingType_Detailed, false);
                }
                dm.EditRebuild3();
                dm.ViewZoomtofit2();
                Save(dm, name + ".SLDDRW");
                sw.CloseDoc(dm.GetPathName());
            }
            catch (Exception ex)
            {
                Console.WriteLine("   Чертёж " + name + " не создан: " + ex.Message);
            }
        }

        static ModelDoc2 NewDocA3()
        {
            string tpl = sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
            var m = (ModelDoc2)sw.NewDocument(tpl, (int)swDwgPaperSizes_e.swDwgPaperA3size, 0.42, 0.297);
            if (m == null) throw new Exception("нет шаблона чертежа «" + tpl + "»");
            return m;
        }

        static string BomTemplate()
        {
            string root = sw.GetExecutablePath();
            foreach (var lang in new[] { "english", "russian" })
            {
                string t = Path.Combine(root, "lang", lang, "bom-standard.sldbomtbt");
                if (File.Exists(t)) return t;
            }
            return "";
        }
    }
}
