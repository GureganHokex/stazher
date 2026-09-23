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
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        static bool K(Key k) { return Keyboard.current != null && Keyboard.current[k].isPressed; }
        static bool KD(Key k) { return Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame; }
        public static Vector2 Move()
        {
            float x = (K(Key.D) ? 1 : 0) - (K(Key.A) ? 1 : 0);
            float y = (K(Key.W) ? 1 : 0) - (K(Key.S) ? 1 : 0);
            return new Vector2(x, y);
        }
        public static Vector2 Look() { return Mouse.current == null ? Vector2.zero : Mouse.current.delta.ReadValue() * 0.08f; }
        public static bool Sprint() { return K(Key.LeftShift); }
        public static bool Jump() { return KD(Key.Space); }
        public static bool Interact() { return KD(Key.E); }
        public static bool Esc() { return KD(Key.Escape); }
        public static bool Click() { return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame; }
        public static bool ToggleView() { return KD(Key.V); }
#else
        public static Vector2 Move()
        {
            float x = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            float y = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            return new Vector2(x, y);
        }
        public static Vector2 Look() { return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 2f; }
        public static bool Sprint() { return Input.GetKey(KeyCode.LeftShift); }
        public static bool Jump() { return Input.GetKeyDown(KeyCode.Space); }
        public static bool Interact() { return Input.GetKeyDown(KeyCode.E); }
        public static bool Esc() { return Input.GetKeyDown(KeyCode.Escape); }
        public static bool Click() { return Input.GetMouseButtonDown(0); }
        public static bool ToggleView() { return Input.GetKeyDown(KeyCode.V); }
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
