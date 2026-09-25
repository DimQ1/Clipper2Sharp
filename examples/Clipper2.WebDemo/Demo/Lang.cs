using System.Collections.Generic;

namespace Clipper2.WebDemo.Demo;

/// <summary>
/// The two languages of the page. English is the default (the page is linked from
/// the NuGet package), Russian is one click away.
/// </summary>
public static class Lang
{
  public const string English = "en";
  public const string Russian = "ru";

  private static readonly Dictionary<string, (string En, string Ru)> Text = new()
  {
    ["title"] = ("Clipper2Sharp — live demo", "Clipper2Sharp — живая демонстрация"),
    ["subtitle"] = (
      "Polygon clipping, offsetting, rectangle clipping and triangulation, running as WebAssembly in this tab. Drag the shapes and run an operation.",
      "Отсечение полигонов, смещение (offset), прямоугольное отсечение и триангуляция — прямо в этой вкладке, как WebAssembly. Тащите фигуры мышью и запускайте операции."),
    ["shapes"] = ("Shapes", "Фигуры"),
    ["subject"] = ("Subject (blue)", "Subject (синяя)"),
    ["clip"] = ("Clip (orange)", "Clip (оранжевая)"),
    ["randomize"] = ("Random shapes", "Случайные фигуры"),
    ["reset"] = ("Reset", "Сброс"),
    ["operation"] = ("Operation", "Операция"),
    ["fillRule"] = ("Fill rule", "Правило заливки"),
    ["delta"] = ("offset delta", "offset delta"),
    ["epsilon"] = ("simplify epsilon", "simplify epsilon"),
    ["join"] = ("join", "стык (join)"),
    ["repetitions"] = ("runs to time", "повторов замера"),
    ["dragHint"] = (
      "Drag a blue vertex handle to edit the subject, drag anywhere else to move the clip shape.",
      "Тащите синий маркер вершины, чтобы править subject; тащите в любом другом месте, чтобы двигать clip-фигуру."),
    ["results"] = ("Result of the last run", "Результат последнего запуска"),
    ["time"] = ("time", "время"),
    ["allocation"] = ("allocated / run", "аллокация / запуск"),
    ["paths"] = ("paths", "путей"),
    ["points"] = ("points", "точек"),
    ["area"] = ("area", "площадь"),
    ["input"] = ("input", "на входе"),
    ["vertices"] = ("vertices", "вершин"),
    ["speed"] = ("Speed", "Скорость"),
    ["speedIntro"] = (
      "Union of an increasing number of overlapping shapes (32 vertices each) - one sweep over every path, measured in this tab.",
      "Объединение (Union) растущего числа перекрывающихся фигур (по 32 вершины) — один проход по всем путям, замер прямо в этой вкладке."),
    ["runScaling"] = ("Measure the scaling", "Замерить масштабирование"),
    ["running"] = ("measuring…", "идёт замер…"),
    ["chart"] = ("time, ms", "время, мс"),
    ["workload"] = ("shapes", "фигур"),
    ["allocatedTotal"] = ("allocated", "аллокация"),
    ["resultPaths"] = ("result paths", "путей в результате"),
    ["headline"] = ("Result of the measurement", "Итог замера"),
    ["footnote"] = (
      "Times depend on the machine and on the build: the badge above says whether this is the interpreted or the AOT-compiled WebAssembly build, and the browser only gives the app a single thread, so the library's multi-threaded paths stay sequential here. Allocations are deterministic.",
      "Время зависит от машины и от сборки: значок выше показывает, это интерпретируемая или AOT-скомпилированная сборка WebAssembly; браузер даёт приложению один поток, поэтому многопоточные ветки библиотеки здесь работают последовательно. Аллокации детерминированы."),
    ["nugget"] = ("NuGet package", "Пакет NuGet"),
    ["sources"] = ("Sources", "Исходники"),
    ["buildAot"] = ("AOT", "AOT"),
    ["buildInterpreted"] = ("interpreted", "интерпретатор"),
    ["singleThread"] = ("1 thread", "1 поток"),
    ["framework"] = ("runtime", "рантайм"),
    ["updated"] = ("World coordinates: ", "Мировые координаты: "),
    ["opUnion"] = ("Union", "Union"),
    ["opIntersect"] = ("Intersect", "Intersect"),
    ["opDifference"] = ("Difference", "Difference"),
    ["opXor"] = ("Xor", "Xor"),
    ["opOffset"] = ("Offset (inflate)", "Offset (смещение)"),
    ["opSimplify"] = ("Simplify", "Simplify"),
    ["opRectClip"] = ("Rect clip", "Rect clip"),
    ["opTriangulate"] = ("Triangulate", "Triangulate"),
    ["opPip"] = ("Point in polygon", "Точка в полигоне"),
    ["note"] = ("note", "примечание"),
    ["noResult"] = ("nothing left after the operation", "после операции ничего не осталось"),
    ["triangles"] = ("triangles", "треугольников"),
    ["probes"] = ("probes", "точек сетки"),
    ["inside"] = ("inside", "внутри"),
    ["showInput"] = ("show the input", "показывать входные фигуры"),
    ["opInfoUnion"] = (
      "Both shapes together, with the fill rule deciding what an overlap means.",
      "Объединение двух фигур; правило заливки решает, что считается перекрытием."),
    ["opInfoIntersect"] = ("Only the area covered by both shapes.", "Только пересечение: область, покрытая обеими фигурами."),
    ["opInfoDifference"] = ("Subject minus clip. Not commutative, unlike the others.", "Subject минус clip. Не коммутативно, в отличие от остальных."),
    ["opInfoXor"] = ("The area covered by exactly one of the two shapes.", "Область, покрытая ровно одной из фигур."),
    ["opInfoOffset"] = ("Moves every edge outwards (or inwards with a negative delta) and joins the corners.", "Смещает каждое ребро наружу (или внутрь при отрицательной дельте) и соединяет углы."),
    ["opInfoSimplify"] = ("Removes vertices that stay within epsilon of the line between their neighbours.", "Убирает вершины, отклоняющиеся от линии между соседями меньше, чем на epsilon."),
    ["opInfoRectClip"] = ("Clips the subject with the dashed rectangle - the clip shape's bounding box.", "Отсекает subject пунктирным прямоугольником — ограничивающей рамкой clip-фигуры."),
    ["opInfoTriangulate"] = ("Splits the subject into triangles (Delaunay legalisation on).", "Разбивает subject на треугольники (с линеаризацией по Делоне)."),
    ["opInfoPip"] = ("Asks \"is this point inside?\" for a whole grid of points and shows both ways of asking it.", "Отвечает на вопрос «точка внутри?» для всей сетки точек и показывает оба способа спросить."),

    ["animTitle"] = ("Animation — what the library is for", "Анимация — зачем нужна библиотека"),
    ["animIntro"] = (
      "Each scenario animates one input of one operation, so the operation explains itself: what moves, what grows, what disappears, what gets merged.",
      "Каждый сценарий анимирует один вход одной операции, поэтому операция объясняет себя сама: что движется, что растёт, что исчезает, что объединяется."),
    ["animPlay"] = ("Play", "Запустить"),
    ["animPause"] = ("Pause", "Пауза"),
    ["animSpeed"] = ("speed", "скорость"),
    ["animFrame"] = ("frame", "кадр"),
    ["animFps"] = ("fps", "fps"),
    ["animNote"] = (
      "The frame time is the library's real cost in this browser, recomputed from scratch for every frame (no caching).",
      "Время кадра — это реальная стоимость библиотеки в этом браузере, всё считается с нуля на каждом кадре (без кеширования)."),
    ["scenMovingClip"] = ("Moving clip", "Движущийся clip"),
    ["scenClipWindow"] = ("Sliding window", "Скользящее окно"),
    ["scenGrowingOffset"] = ("Growing offset", "Растущий offset"),
    ["scenSimplifying"] = ("Simplifying", "Упрощение"),
    ["scenGrowingUnion"] = ("Union build-up", "Нарастающий Union"),
    ["whyMovingClip"] = (
      "The shape you need is the exact overlap of two shapes that keep moving: a CAD drag, a fit test, two moving contours. The intersection is recomputed every frame, and the green area is the answer.",
      "Нужно точное пересечение двух фигур, которые движутся: перетаскивание в CAD, проверка стыковки, два движущихся контура. Пересечение пересчитывается на каждом кадре, зелёная область — ответ."),
    ["whyClipWindow"] = (
      "Only what is inside the window matters. Rectangle clipping is the cheap way to get that: no sweep, no intersections, just the visible part of every path.",
      "Важно только то, что попало в окно. Прямоугольное отсечение — дешёвый способ это получить: без прохода по AEL и без пересечений, только видимая часть каждого пути."),
    ["whyGrowingOffset"] = (
      "A contour a fixed distance away: a toolpath, a clearance zone, a hit box. The joins are what makes it more than scaling - corners stay sharp or round the way you ask.",
      "Контур на фиксированном расстоянии: траектория инструмента, зона зазора, хитбокс. Стыки (joins) отличают это от простого масштабирования — углы остаются острыми или скруглёнными, как вы зададите."),
    ["whySimplifying"] = (
      "Fewer points for the same shape: vertices that stay within epsilon of the line between their neighbours carry no information and disappear.",
      "Меньше точек при той же форме: вершины, отклоняющиеся от линии между соседями меньше чем на epsilon, информации не несут и исчезают."),
    ["whyGrowingUnion"] = (
      "One outline instead of many overlapping ones. Watch the sweep absorb each new shape and the path count stop growing.",
      "Один контур вместо множества перекрывающихся. Смотрите, как проход поглощает каждую новую фигуру, а число контуров перестаёт расти."),
    ["scenarioHint"] = (
      "The animation plays the operation the scenario is about; the controls above it stay available when nothing is playing.",
      "Анимация сама включает нужную операцию; элементы управления остаются доступны, когда анимация не запущена."),
    ["notMeasured"] = ("n/a", "н/д"),
    ["allocUnavailable"] = (
      "This runtime did not report an allocation count, so the cell reads n/a instead of a zero that would not be true.",
      "Этот рантайм не отдал счётчик аллокаций, поэтому в ячейке стоит н/д, а не ноль, который был бы неправдой."),
    ["allocationNote"] = (
      "A single operation allocates less than the runtime updates its counter, which is why the per-run figure shown during an animation is the average of the last eight operations (they are read around the library calls, so rendering is not counted). A manual run with several repetitions reports its own measurement.",
      "Одна операция аллоцирует меньше шага обновления счётчика рантайма, поэтому во время анимации показывается среднее по последним восьми операциям (замер идёт вокруг вызовов библиотеки, рендеринг не учитывается). Ручной запуск с несколькими повторами показывает собственный замер.")
  };

  public static string Get(string lang, string key)
  {
    if (!Text.TryGetValue(key, out (string En, string Ru) pair)) return key;
    return lang == Russian ? pair.Ru : pair.En;
  }

  public static string Other(string lang) => lang == Russian ? English : Russian;

  public static string Badge(string lang) => lang == Russian ? "RU" : "EN";
}
