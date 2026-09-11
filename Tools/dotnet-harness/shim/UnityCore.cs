// Test-harness shim for the slice of UnityEngine that Brink's runtime uses.
// Not a Unity emulator: just enough surface to compile and run the edit-mode
// suite outside the editor. JsonUtility below is the one piece that has to be
// behaviourally faithful, because save/load tests depend on its exact rules.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnityEngine
{
    public class SerializeField : Attribute { }
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { }
    }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, SubsystemRegistration, AfterAssembliesLoaded, BeforeSplashScreen }

    public static class Debug
    {
        public static void Log(object m) { }
        public static void LogWarning(object m) { }
        public static void LogError(object m) { }
        public static void LogException(Exception e) { }
        public static bool isDebugBuild => true;
    }

    public static class Mathf
    {
        public const float Epsilon = 1.401298E-45f;
        public const float PI = 3.14159274f;
        public const float Infinity = float.PositiveInfinity;
        public static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        public static int Clamp(int v, int a, int b) => v < a ? a : (v > b ? b : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Abs(float v) => Math.Abs(v);
        public static int Abs(int v) => Math.Abs(v);
        public static float Round(float v) => (float)Math.Round(v, MidpointRounding.AwayFromZero);
        public static int RoundToInt(float v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static float Ceil(float v) => (float)Math.Ceiling(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => Math.Abs(b - a) < 1e-9f ? 0f : Clamp01((v - a) / (b - a));
        public static float Sign(float v) => v < 0f ? -1f : 1f;
        public static bool Approximately(float a, float b) => Math.Abs(b - a) < 1e-6f;
        public static float Log(float v) => (float)Math.Log(v);
        public static float Exp(float v) => (float)Math.Exp(v);
        public static float Log10(float v) => (float)Math.Log10(v);
        public static float Log(float v, float b) => (float)Math.Log(v, b);
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public float xMin => x; public float yMin => y;
        public float xMax => x + width; public float yMax => y + height;
    }

    public static class Screen
    {
        public static int width = 2340;
        public static int height = 1080;
        public static float dpi = 0f;
        public static Rect safeArea = new Rect(0f, 0f, 2340f, 1080f);
    }

    public static class Application
    {
        public static bool isEditor => true;
        public static bool isPlaying => false;
        public static bool isBatchMode => true;
        public static string persistentDataPath =>
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "brink-harness-data");
        public static string dataPath => persistentDataPath;
        public static RuntimePlatform platform => RuntimePlatform.LinuxEditor;
        public static bool isMobilePlatform => false;
        public static int targetFrameRate { get; set; } = -1;
        public static string productName => "Brink";
        public static string version => "0.1.0";
        public static event Action quitting;
        public static void Quit() { quitting?.Invoke(); }
    }

    public enum RuntimePlatform { LinuxEditor, WindowsEditor, OSXEditor, Android, IPhonePlayer }

    public static class PlayerPrefs
    {
        static readonly Dictionary<string, object> store = new Dictionary<string, object>();
        public static void SetInt(string k, int v) => store[k] = v;
        public static void SetFloat(string k, float v) => store[k] = v;
        public static void SetString(string k, string v) => store[k] = v;
        public static int GetInt(string k, int d = 0) => store.TryGetValue(k, out var v) ? Convert.ToInt32(v) : d;
        public static float GetFloat(string k, float d = 0f) => store.TryGetValue(k, out var v) ? Convert.ToSingle(v) : d;
        public static string GetString(string k, string d = "") => store.TryGetValue(k, out var v) ? Convert.ToString(v) : d;
        public static bool HasKey(string k) => store.ContainsKey(k);
        public static void DeleteKey(string k) => store.Remove(k);
        public static void DeleteAll() => store.Clear();
        public static void Save() { }
    }

    public class Transform : Component
    {
        public Transform parent { get; set; }
        public Vector2 position { get; set; }
        public void SetParent(Transform t) { parent = t; }
        public void SetParent(Transform t, bool worldPositionStays) { parent = t; }
    }

    public class Object
    {
        public string name = "";
        public static void DontDestroyOnLoad(Object o) { }
        public static void Destroy(Object o) { }
        public static void DestroyImmediate(Object o) { }
    }
    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
    }
    public class GameObject : Object
    {
        Transform tf;
        public Transform transform => tf ??= new Transform();
        public bool activeSelf { get; private set; } = true;
        public void SetActive(bool v) { activeSelf = v; }
        public GameObject() { }
        public GameObject(string n) { name = n; }
        public GameObject(string n, params Type[] components) { name = n; }
        public T AddComponent<T>() where T : Component, new() => new T();
        public static GameObject Find(string n) => null;
        public static void DontDestroyOnLoad(Object o) { }
    }
    public class Component : Object
    {
        GameObject go;
        public GameObject gameObject => go ??= new GameObject();
        public Transform transform => gameObject.transform;
    }
    public class MonoBehaviour : Component
    {
        public bool enabled { get; set; } = true;
        public Coroutine StartCoroutine(System.Collections.IEnumerator routine)
        {
            // Drain synchronously: the harness has no frame loop, and every
            // coroutine in this project is a fade or a delay whose end state is
            // what a test would look at.
            try { while (routine != null && routine.MoveNext()) { } } catch { }
            return new Coroutine();
        }
        public void StopCoroutine(Coroutine c) { }
        public void StopCoroutine(System.Collections.IEnumerator c) { }
        public void StopAllCoroutines() { }
        public void Invoke(string m, float t) { }
        public void CancelInvoke() { }
        public T GetComponent<T>() where T : Component, new() => new T();
        public T GetComponentInChildren<T>() where T : Component, new() => new T();
        public bool TryGetComponent<T>(out T c) where T : Component, new() { c = new T(); return true; }
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : class => null;
        public static Object Load(string path) => null;
        public static T[] LoadAll<T>(string path) where T : class => new T[0];
    }

    public class AudioClip : Object { public float length => 1f; }
    public class AudioSource : Component
    {
        public AudioClip clip; public float volume = 1f; public bool loop; public bool playOnAwake;
        public bool isPlaying; public float pitch = 1f; public float time;
        public Audio.AudioMixerGroup outputAudioMixerGroup;
        public void Play() { isPlaying = true; }
        public void Stop() { isPlaying = false; }
        public void PlayOneShot(AudioClip c, float v = 1f) { }
        public void Pause() { }
        public void UnPause() { }
        public float spatialBlend { get; set; }
        public bool mute { get; set; }
        public bool ignoreListenerPause { get; set; }
    }

    /// <summary>
    /// Faithful enough to Unity's JsonUtility for the save tests to mean
    /// something: public instance fields only, no properties, enums as ints,
    /// lists and nested [Serializable] types by value — and, critically, **a key
    /// absent from the JSON leaves the constructed default in place**, which is
    /// the exact rule the old-save compatibility test relies on.
    /// </summary>
    public static class JsonUtility
    {
        public static string ToJson(object obj) => ToJson(obj, false);

        public static string ToJson(object obj, bool prettyPrint)
        {
            if (obj == null) return "{}";
            var node = Write(obj);
            return node.ToJsonString(new JsonSerializerOptions { WriteIndented = prettyPrint });
        }

        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default;
            var node = JsonNode.Parse(json);
            var target = Activator.CreateInstance(typeof(T));
            ReadInto(target, node as JsonObject);
            return (T)target;
        }

        public static void FromJsonOverwrite(string json, object target)
        {
            if (string.IsNullOrWhiteSpace(json) || target == null) return;
            ReadInto(target, JsonNode.Parse(json) as JsonObject);
        }

        static IEnumerable<FieldInfo> Fields(Type t)
        {
            for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
                foreach (var f in cur.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                                | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (f.IsStatic || f.IsInitOnly) continue;
                    if (f.IsNotSerialized) continue;
                    bool serialized = f.IsPublic || f.GetCustomAttribute<SerializeField>() != null;
                    if (!serialized) continue;
                    if (f.Name.Contains("k__BackingField")) continue;
                    yield return f;
                }
        }

        static bool IsLeaf(Type t) =>
            t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal);

        static JsonNode Write(object obj)
        {
            var type = obj.GetType();
            var o = new JsonObject();
            foreach (var f in Fields(type))
            {
                var v = f.GetValue(obj);
                o[f.Name] = WriteValue(f.FieldType, v);
            }
            return o;
        }

        static JsonNode WriteValue(Type type, object v)
        {
            if (v == null) return type == typeof(string) ? JsonValue.Create("") : null;
            if (type.IsEnum) return JsonValue.Create(Convert.ToInt32(v));
            if (type == typeof(string)) return JsonValue.Create((string)v);
            if (type == typeof(bool)) return JsonValue.Create((bool)v);
            if (type == typeof(float)) return JsonValue.Create((float)v);
            if (type == typeof(double)) return JsonValue.Create((double)v);
            if (type == typeof(int)) return JsonValue.Create((int)v);
            if (type == typeof(long)) return JsonValue.Create((long)v);
            if (type.IsPrimitive) return JsonValue.Create(Convert.ToDouble(v));

            if (v is IList list && type != typeof(string))
            {
                var elem = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                var arr = new JsonArray();
                foreach (var item in list) arr.Add(WriteValue(elem, item));
                return arr;
            }
            return Write(v);
        }

        static void ReadInto(object target, JsonObject src)
        {
            if (target == null || src == null) return;
            foreach (var f in Fields(target.GetType()))
            {
                if (!src.TryGetPropertyValue(f.Name, out var node) || node == null) continue; // absent => keep default
                var value = ReadValue(f.FieldType, node, f.GetValue(target));
                f.SetValue(target, value);
            }
        }

        static object ReadValue(Type type, JsonNode node, object existing)
        {
            if (type.IsEnum) return Enum.ToObject(type, node.GetValue<int>());
            if (type == typeof(string)) return node.GetValue<string>();
            if (type == typeof(bool)) return node.GetValue<bool>();
            if (type == typeof(float)) return (float)node.GetValue<double>();
            if (type == typeof(double)) return node.GetValue<double>();
            if (type == typeof(int)) return node.GetValue<int>();
            if (type == typeof(long)) return node.GetValue<long>();
            if (type.IsPrimitive) return Convert.ChangeType(node.GetValue<double>(), type);

            if (node is JsonArray arr)
            {
                if (type.IsArray)
                {
                    var elem = type.GetElementType();
                    var made = Array.CreateInstance(elem, arr.Count);
                    for (int i = 0; i < arr.Count; i++) made.SetValue(ReadValue(elem, arr[i], null), i);
                    return made;
                }
                var itemType = type.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(type);
                for (int i = 0; i < arr.Count; i++) list.Add(ReadValue(itemType, arr[i], null));
                return list;
            }

            var obj = existing ?? Activator.CreateInstance(type);
            // Value types must be boxed once and written back, or field writes vanish.
            if (type.IsValueType) obj = Activator.CreateInstance(type);
            ReadInto(obj, node as JsonObject);
            return obj;
        }
    }
}

namespace UnityEngine.Audio
{
    public class AudioMixerGroup : UnityEngine.Object { }
    public class AudioMixer : UnityEngine.Object
    {
        readonly Dictionary<string, float> values = new Dictionary<string, float>();
        readonly HashSet<string> exposed = new HashSet<string>();
        public void ExposeForTests(string name) => exposed.Add(name);
        public bool SetFloat(string name, float v)
        {
            if (!exposed.Contains(name)) return false;
            values[name] = v; return true;
        }
        public bool GetFloat(string name, out float v) => values.TryGetValue(name, out v);
        public AudioMixerGroup[] FindMatchingGroups(string path) => new AudioMixerGroup[0];
    }
}

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class HeaderAttribute : Attribute { public HeaderAttribute(string h) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class TooltipAttribute : Attribute { public TooltipAttribute(string t) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class TextAreaAttribute : Attribute
    {
        public TextAreaAttribute() { }
        public TextAreaAttribute(int min, int max) { }
    }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class SpaceAttribute : Attribute
    {
        public SpaceAttribute() { }
        public SpaceAttribute(float h) { }
    }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class CreateAssetMenuAttribute : Attribute
    {
        public string fileName { get; set; }
        public string menuName { get; set; }
        public int order { get; set; }
    }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class RequireComponentAttribute : Attribute
    {
        public RequireComponentAttribute(Type a) { }
        public RequireComponentAttribute(Type a, Type b) { }
        public RequireComponentAttribute(Type a, Type b, Type c) { }
    }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class DisallowMultipleComponentAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class DefaultExecutionOrderAttribute : Attribute { public DefaultExecutionOrderAttribute(int o) { } }

    public class Coroutine { }

    /// <summary>Deterministic stand-in. Brink's simulation must never use this —
    /// a test asserts as much — so only debug/UI paths reach it.</summary>
    public static class Random
    {
        static System.Random rng = new System.Random(12345);
        public static void InitState(int seed) { rng = new System.Random(seed); }
        public static float value => (float)rng.NextDouble();
        public static float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        public static int Range(int a, int b) => rng.Next(a, b);
    }
    public class YieldInstruction { }
    public class WaitForSeconds : YieldInstruction { public WaitForSeconds(float s) { } }
    public class WaitForSecondsRealtime : YieldInstruction { public WaitForSecondsRealtime(float s) { } }
    public class WaitForEndOfFrame : YieldInstruction { }

    public static class Time
    {
        public static float deltaTime => 0.016f;
        public static float unscaledDeltaTime => 0.016f;
        public static float time => 0f;
        public static float realtimeSinceStartup => 0f;
    }
}
