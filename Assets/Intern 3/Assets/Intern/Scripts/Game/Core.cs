// Ввод, данные задач, сохранения, ранги.
using System;
using System.Collections.Generic;
using UnityEngine;
using Intern.Py;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace Intern.Game
{
    // Работает и со старым Input Manager, и с новым Input System (по умолчанию в Unity 6).
    public static class InputX
    {
        public static float LookScale = 1f;   // чувствительность мыши (настройки)
        public static bool InvertY;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        static bool K(Key k) { return Keyboard.current != null && Keyboard.current[k].isPressed; }
        static bool KD(Key k) { return Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame; }
        public static Vector2 Move()
        {
            float x = (K(Key.D) ? 1 : 0) - (K(Key.A) ? 1 : 0);
            float y = (K(Key.W) ? 1 : 0) - (K(Key.S) ? 1 : 0);
            return new Vector2(x, y);
        }
        public static Vector2 Look()
        {
            if (Mouse.current == null) return Vector2.zero;
            var d = Mouse.current.delta.ReadValue() * 0.08f * LookScale;
            if (InvertY) d.y = -d.y;
            return d;
        }
        public static bool Sprint() { return K(Key.LeftShift); }
        public static bool Jump() { return KD(Key.Space); }
        public static bool Interact() { return KD(Key.E); }
#if UNITY_EDITOR
        public static bool Esc() { return KD(Key.Escape) || KD(Key.F4); }   // в редакторе F4 дублирует Esc (удалённое управление Esc не передаёт)
#else
        public static bool Esc() { return KD(Key.Escape); }
#endif
        public static bool Click() { return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame; }
        public static bool ToggleView() { return KD(Key.V); }
        public static Vector2 MousePosition() { return Mouse.current == null ? Vector2.zero : Mouse.current.position.ReadValue(); }
        public static bool DebugSit() { return KD(Key.F8); }
        public static bool DebugDump() { return KD(Key.F7); }
        public static bool DebugClose() { return KD(Key.F6); }
        public static bool DebugCareer() { return KD(Key.F9); }
        public static bool DebugHour() { return KD(Key.F3); }
        public static bool DebugDoor() { return KD(Key.F1); }
        public static bool DebugLunchEnd() { return KD(Key.F2); }
        public static bool Screenshot() { return KD(Key.F12); }
        // Был ли хоть какой-то ввод в этом кадре — для автопаузы
        public static bool AnyInput()
        {
            var kb = Keyboard.current; var m = Mouse.current;
            if (kb != null && kb.anyKey.isPressed) return true;
            if (m != null && (m.leftButton.isPressed || m.rightButton.isPressed || m.delta.ReadValue().sqrMagnitude > 0.5f || m.scroll.ReadValue().sqrMagnitude > 0.01f)) return true;
            return false;
        }
        public static bool Attack() { return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame; }
#else
        public static Vector2 Move()
        {
            float x = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            float y = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            return new Vector2(x, y);
        }
        public static Vector2 Look()
        {
            var d = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 2f * LookScale;
            if (InvertY) d.y = -d.y;
            return d;
        }
        public static bool Sprint() { return Input.GetKey(KeyCode.LeftShift); }
        public static bool Jump() { return Input.GetKeyDown(KeyCode.Space); }
        public static bool Interact() { return Input.GetKeyDown(KeyCode.E); }
        public static bool Esc() { return Input.GetKeyDown(KeyCode.Escape); }
        public static bool Click() { return Input.GetMouseButtonDown(0); }
        public static bool ToggleView() { return Input.GetKeyDown(KeyCode.V); }
        public static Vector2 MousePosition() { return Input.mousePosition; }
        public static bool DebugSit() { return Input.GetKeyDown(KeyCode.F8); }
        public static bool DebugDump() { return Input.GetKeyDown(KeyCode.F7); }
        public static bool DebugClose() { return Input.GetKeyDown(KeyCode.F6); }
        public static bool DebugCareer() { return Input.GetKeyDown(KeyCode.F9); }
        public static bool DebugHour() { return Input.GetKeyDown(KeyCode.F3); }
        public static bool DebugDoor() { return Input.GetKeyDown(KeyCode.F1); }
        public static bool DebugLunchEnd() { return Input.GetKeyDown(KeyCode.F2); }
        public static bool Screenshot() { return Input.GetKeyDown(KeyCode.F12); }
        public static bool AnyInput() { return Input.anyKey || Mathf.Abs(Input.GetAxisRaw("Mouse X")) + Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.01f; }
        public static bool Attack() { return Input.GetMouseButtonDown(0); }
#endif
    }

    public enum Difficulty { Easy = 0, Medium = 1, Hard = 2 }

    [Serializable]
    public class TaskData
    {
        public string id, chapter, title, sender, story, theory, goal, starter, solution;
        public string[] hints = new string[0];
        public TestCase[] tests = new TestCase[0];
        public int deadline = 240, reward = 100;
        public bool isBugHunt;

        // --- задачи направлений (Resources/Tasks/tracks/*.json) ---
        public string type = "write_code";   // quiz find_bug write_code code_review architecture incident estimation
        public string language = "python";   // python javascript typescript tsx jsx sql yaml bash dockerfile html css nginx hcl promql text
        public string entry;                 // функция для проверки (python/javascript), пусто — программа (stdin/stdout)
        public string track, topic, grade, character, key, explanation;
        public int difficulty = 1, xp = 20, timeLimit;   // timeLimit — минуты (0 — без таймера)
        public string[] options;             // варианты ответа (задачи с выбором)
        public int[] answer;                 // индексы верных вариантов
        public bool multi;                   // верных несколько
        public bool legacy;
        public string legacyId;              // id задачи в старом python.json (py01…) — для переноса сохранений
        [NonSerialized] public List<object> testCases;   // test_cases из JSON как есть

        public bool IsChoice { get { return options != null && options.Length > 0; } }
        // Как проверяется: choice | py | js | sql | static
        public string Mode
        {
            get
            {
                if (IsChoice) return "choice";
                switch (language)
                {
                    case "python": return "py";
                    case "javascript": return "js";
                    case "sql": return "sql";
                    default: return "static";
                }
            }
        }
    }

    [Serializable]
    public class TaskFile { public string language; public TaskData[] tasks = new TaskData[0]; }

    [Serializable]
    public class SaveData
    {
        public int difficulty = 0;
        public int money = 0;
        public int bugsCaught = 0;
        public bool hasDuck, hasMonitor;
        public List<string> done = new List<string>();
        public List<string> codeIds = new List<string>();
        public List<string> codeTexts = new List<string>();
        public Appearance look = new Appearance();
        public List<string> owned = new List<string>();
        public bool firstPerson;
        public bool hasCharacter;
        public string profession = "";   // backend frontend devops fullstack
        public int xp;
        public int version;              // 2 — id задач из направлений, 3 — рабочий день

        // ---- рабочий день (версия 3) ----
        public int day = 1;              // номер рабочего дня, с 1 (Пн)
        public float minute = 540;       // время в офисе: минуты от полуночи, 9:00 = 540
        public bool lunchTaken;          // обед сегодня уже был
        public float satedUntil;         // до этой минуты действует бонус «Сытый»
        public int strikes, cleanDays;   // выговоры и чистые дни подряд (5 чистых — минус выговор)
        public bool strikeToday, workedToday;
        public int hourStart = 540;      // начало текущего учётного часа
        public float hourMinutes, hourEditSec;
        public bool hourWorked;
        public int dayTasks, dayXp, dayMoney, dayLunchMoney, dayKills, dayFines, dayWorkHours, dayIdleHours;
        public int totalKills, totalLunches, totalFines;

        public string GetCode(string id) { int i = codeIds.IndexOf(id); return i >= 0 ? codeTexts[i] : null; }
        public void SetCode(string id, string code)
        {
            int i = codeIds.IndexOf(id);
            if (i >= 0) codeTexts[i] = code; else { codeIds.Add(id); codeTexts.Add(code); }
        }
    }

    public static class Progress
    {
        const string Key = "intern_save_v1";

        public static bool HasSave() { return PlayerPrefs.HasKey(Key); }
        public static SaveData Load()
        {
            if (!HasSave()) return new SaveData();
            try { return JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(Key)) ?? new SaveData(); }
            catch { return new SaveData(); }
        }
        public static void Save(SaveData d) { PlayerPrefs.SetString(Key, JsonUtility.ToJson(d)); PlayerPrefs.Save(); }
        public static void Wipe() { PlayerPrefs.DeleteKey(Key); }

        public static string Rank(int done, int total)
        {
            if (done >= total && total > 0) return "Junior+";
            if (done >= 10) return "Junior+";
            if (done >= 5) return "Junior";
            return "Стажёр";
        }

        public static string DifficultyName(Difficulty d)
        {
            switch (d) { case Difficulty.Easy: return "Лёгкая"; case Difficulty.Medium: return "Средняя"; default: return "Тяжёлая"; }
        }

        public static TaskFile LoadTasks(string language)
        {
            var ta = Resources.Load<TextAsset>("Tasks/" + language);
            if (ta == null) { Debug.LogError("Не найден файл задач Resources/Tasks/" + language + ".json"); return new TaskFile(); }
            return JsonUtility.FromJson<TaskFile>(ta.text);
        }
    }
}
