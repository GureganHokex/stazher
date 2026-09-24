using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Intern.Game;
using Intern.Py;

public static class TracksTest
{
    static Dictionary<string, TrackData> data = new Dictionary<string, TrackData>();
    static TrackData Load(string n) { return data[n]; }

    public static int Main(string[] args)
    {
        int fails = 0;
        foreach (var n in Tracks.All) data[n] = Tracks.Parse(File.ReadAllText("Assets/Intern 3/Assets/Intern/Resources/Tasks/tracks/" + n + ".json"), n);
        foreach (var n in Tracks.All) Console.WriteLine(n + ": topics " + data[n].topics.Count + ", tasks " + data[n].topics.Sum(t => t.tasks.Count));
        var senders = new Dictionary<string, int>();
        foreach (var n in Tracks.All) foreach (var tp in data[n].topics) foreach (var t in tp.tasks) { senders[t.sender] = senders.ContainsKey(t.sender) ? senders[t.sender] + 1 : 1; }
        Console.WriteLine("senders: " + string.Join("; ", senders.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value).ToArray()));
        var rnd = new Random(3);
        var allTasks = Tracks.All.SelectMany(n => data[n].topics.SelectMany(tp => tp.tasks)).ToList();
        for (int i = 0; i < 6; i++) { var t = allTasks[rnd.Next(allTasks.Count)]; Console.WriteLine("  [" + t.key + " " + t.sender + "] " + t.story.Substring(0, Math.Min(140, t.story.Length))); }
        var legacy = Tracks.LegacyMap(Load);
        Console.WriteLine("legacy map: " + legacy.Count + " " + string.Join(",", legacy.Take(3).Select(kv => kv.Key + "->" + kv.Value).ToArray()));

        foreach (var prof in Professions.Ids)
        {
            var path = Tracks.BuildPath(prof, Load);
            var done = new HashSet<string>();
            int g = path.GradeIndex(done), steps = 0;
            var trans = new List<string> { Grades.Name(g) + "@0" };
            var order = new List<string>();
            while (true)
            {
                var cur = path.Current(done);
                if (cur == null) break;
                if (!path.TaskOpen(cur, done)) { Console.WriteLine("  !! current not open " + cur.id); fails++; }
                done.Add(cur.id); steps++;
                if (order.Count < 400) order.Add(cur.key);
                int ng = path.GradeIndex(done);
                if (ng != g) { trans.Add((ng >= 4 ? "DONE" : Grades.Name(ng)) + "@" + steps); g = ng; }
                if (steps > 2000) { Console.WriteLine("  !! loop"); fails++; break; }
            }
            Console.WriteLine(prof + ": tasks " + path.Tasks.Length + ", steps " + steps + ", " + string.Join(" → ", trans.ToArray()));
            Console.WriteLine("  topics: " + string.Join(" ", path.Topics.Select(t => t.id + "(" + t.gradeIndex + ")").ToArray()));
            if (steps != path.Tasks.Length) { Console.WriteLine("  !! steps mismatch"); fails++; }
        }

        // проверки эталонов
        int py = 0, js = 0, sql = 0, st = 0, ch = 0;
        foreach (var t in allTasks)
        {
            switch (t.Mode)
            {
                case "choice":
                    ch++;
                    if (t.answer == null || t.answer.Length == 0 || t.answer.Any(a => a < 0 || a >= t.options.Length)) { Console.WriteLine("!! choice " + t.id); fails++; }
                    var v = TaskChecks.Choice(t, t.answer.ToList());
                    if (!v.Correct) { Console.WriteLine("!! choice verdict " + t.id); fails++; }
                    break;
                case "static":
                    st++;
                    var ok = TaskChecks.Static(t.solution, t.testCases);
                    var bad = TaskChecks.Static(t.starter, t.testCases);
                    if (ok.Count < 1 || ok.Any(x => !x.Passed)) { Console.WriteLine("!! static solution fails " + t.id + ": " + string.Join(" | ", ok.Where(x => !x.Passed).Select(x => x.Note).ToArray())); fails++; }
                    if (bad.All(x => x.Passed)) { Console.WriteLine("!! static starter passes " + t.id); fails++; }
                    break;
                case "sql":
                    sql++;
                    SqlResult last;
                    var sr = TaskChecks.Sql(t.solution, t.testCases, out last);
                    if (sr.Count < 1 || sr.Any(x => !x.Passed)) { Console.WriteLine("!! sql " + t.id + " " + string.Join(" | ", sr.Select(x => x.Note + "\n" + x.Actual).ToArray())); fails++; }
                    var sb = TaskChecks.Sql(t.starter, t.testCases, out last);
                    if (sb.All(x => x.Passed)) { Console.WriteLine("!! sql starter passes " + t.id); fails++; }
                    break;
                case "py":
                    py++;
                    List<CheckResult> pr;
                    if (string.IsNullOrEmpty(t.entry)) pr = PyRun.Check(t.solution, t.tests);
                    else pr = PyRun.CheckFunction(t.solution, t.entry, t.testCases.Select(x => { var d = (Dictionary<string, object>)x; return new FuncTest(d["input"], d["expected"]); }).ToList());
                    if (pr.Count < 1 || pr.Any(x => !x.Passed)) { Console.WriteLine("!! py " + t.id + " " + string.Join(" | ", pr.Where(x => !x.Passed).Select(x => x.Note + " " + x.Actual).ToArray())); fails++; }
                    break;
                case "js":
                    js++;
                    var jr = JsRun.Check(t.solution, t.entry, t.testCases);
                    JsError je;
                    var jc = TaskChecks.FromJs(jr, out je);
                    if (!jr.AllPassed || jc.Any(x => !x.Passed)) { Console.WriteLine("!! js " + t.id + " " + string.Join(" | ", jc.Where(x => !x.Passed).Select(x => x.Note).ToArray())); fails++; }
                    break;
            }
        }
        Console.WriteLine("checked: choice " + ch + ", static " + st + ", sql " + sql + ", py " + py + ", js " + js + "; fails " + fails);
        return fails == 0 ? 0 : 1;
    }
}
