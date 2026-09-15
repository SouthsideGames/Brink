// Test-harness shim for UnityEngine.UIElements.
//
// Element trees, class lists and the style properties Brink's views actually
// set. Layout is NOT simulated — nothing here measures or renders — so tests
// that walk the tree, read `text`, or check class membership work, and anything
// depending on real layout would not (Brink's width rules are computed in
// `TerminalMetrics` from numbers, not from resolved layout, which is why they
// remain testable here).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine.UIElements
{
    public enum DisplayStyle { Flex, None }
    public enum Position { Relative, Absolute }
    public enum FlexDirection { Column, Row, ColumnReverse, RowReverse }
    public enum Align { Auto, FlexStart, Center, FlexEnd, Stretch }
    public enum Justify { FlexStart, Center, FlexEnd, SpaceBetween, SpaceAround }
    public enum Wrap { NoWrap, Wrap, WrapReverse }
    public enum Overflow { Visible, Hidden }
    public enum WhiteSpace { Normal, NoWrap, Pre, PreWrap }
    public enum TextAnchor { UpperLeft, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft }
    public enum PickingMode { Position, Ignore }
    public enum LengthUnit { Pixel, Percent }
    public enum ScrollViewMode { Vertical, Horizontal, VerticalAndHorizontal }
    public enum Visibility { Visible, Hidden }

    public struct Length
    {
        public float value; public LengthUnit unit;
        public Length(float v) { value = v; unit = LengthUnit.Pixel; }
        public Length(float v, LengthUnit u) { value = v; unit = u; }
        public static Length Percent(float v) => new Length(v, LengthUnit.Percent);
        public static implicit operator Length(float v) => new Length(v);
    }

    public struct StyleLength
    {
        public Length value;
        public StyleLength(Length v) { value = v; }
        public StyleLength(float v) { value = new Length(v); }
        public static implicit operator StyleLength(Length v) => new StyleLength(v);
        public static implicit operator StyleLength(float v) => new StyleLength(v);
    }
    public struct StyleFloat
    {
        public float value;
        public StyleFloat(float v) { value = v; }
        public static implicit operator StyleFloat(float v) => new StyleFloat(v);
    }
    public struct StyleEnum<T> where T : struct
    {
        public T value;
        public StyleEnum(T v) { value = v; }
        public static implicit operator StyleEnum<T>(T v) => new StyleEnum<T>(v);
        public static bool operator ==(StyleEnum<T> a, T b) => Equals(a.value, b);
        public static bool operator !=(StyleEnum<T> a, T b) => !Equals(a.value, b);
        public static bool operator ==(T b, StyleEnum<T> a) => Equals(a.value, b);
        public static bool operator !=(T b, StyleEnum<T> a) => !Equals(a.value, b);
        public override bool Equals(object o) => o is StyleEnum<T> s ? Equals(value, s.value) : Equals(value, o);
        public override int GetHashCode() => value.GetHashCode();
    }
    public struct StyleColor
    {
        public object value;
        public StyleColor(object v) { value = v; }
    }

    public interface IStyle
    {
        StyleLength width { get; set; }
        StyleLength height { get; set; }
        StyleLength maxWidth { get; set; }
        StyleLength maxHeight { get; set; }
        StyleLength minWidth { get; set; }
        StyleLength minHeight { get; set; }
        StyleLength marginTop { get; set; }
        StyleLength marginBottom { get; set; }
        StyleLength marginLeft { get; set; }
        StyleLength marginRight { get; set; }
        StyleLength paddingTop { get; set; }
        StyleLength paddingBottom { get; set; }
        StyleLength paddingLeft { get; set; }
        StyleLength paddingRight { get; set; }
        StyleLength top { get; set; }
        StyleLength bottom { get; set; }
        StyleLength left { get; set; }
        StyleLength right { get; set; }
        StyleLength fontSize { get; set; }
        StyleFloat flexGrow { get; set; }
        StyleFloat flexShrink { get; set; }
        StyleFloat opacity { get; set; }
        StyleEnum<DisplayStyle> display { get; set; }
        StyleEnum<Position> position { get; set; }
        StyleEnum<FlexDirection> flexDirection { get; set; }
        StyleEnum<Align> alignItems { get; set; }
        StyleEnum<Align> alignSelf { get; set; }
        StyleEnum<Justify> justifyContent { get; set; }
        StyleEnum<Wrap> flexWrap { get; set; }
        StyleEnum<Overflow> overflow { get; set; }
        StyleEnum<WhiteSpace> whiteSpace { get; set; }
        StyleEnum<TextAnchor> unityTextAlign { get; set; }
        StyleEnum<Visibility> visibility { get; set; }
        StyleColor color { get; set; }
        StyleColor backgroundColor { get; set; }
        StyleFloat unityParagraphSpacing { get; set; }
    }

    sealed class Style : IStyle
    {
        public StyleLength width { get; set; }
        public StyleLength height { get; set; }
        public StyleLength maxWidth { get; set; }
        public StyleLength maxHeight { get; set; }
        public StyleLength minWidth { get; set; }
        public StyleLength minHeight { get; set; }
        public StyleLength marginTop { get; set; }
        public StyleLength marginBottom { get; set; }
        public StyleLength marginLeft { get; set; }
        public StyleLength marginRight { get; set; }
        public StyleLength paddingTop { get; set; }
        public StyleLength paddingBottom { get; set; }
        public StyleLength paddingLeft { get; set; }
        public StyleLength paddingRight { get; set; }
        public StyleLength top { get; set; }
        public StyleLength bottom { get; set; }
        public StyleLength left { get; set; }
        public StyleLength right { get; set; }
        public StyleLength fontSize { get; set; }
        public StyleFloat flexGrow { get; set; }
        public StyleFloat flexShrink { get; set; }
        public StyleFloat opacity { get; set; }
        public StyleEnum<DisplayStyle> display { get; set; }
        public StyleEnum<Position> position { get; set; }
        public StyleEnum<FlexDirection> flexDirection { get; set; }
        public StyleEnum<Align> alignItems { get; set; }
        public StyleEnum<Align> alignSelf { get; set; }
        public StyleEnum<Justify> justifyContent { get; set; }
        public StyleEnum<Wrap> flexWrap { get; set; }
        public StyleEnum<Overflow> overflow { get; set; }
        public StyleEnum<WhiteSpace> whiteSpace { get; set; }
        public StyleEnum<TextAnchor> unityTextAlign { get; set; }
        public StyleEnum<Visibility> visibility { get; set; }
        public StyleColor color { get; set; }
        public StyleColor backgroundColor { get; set; }
        public StyleFloat unityParagraphSpacing { get; set; }
    }

    public class EventBase { }
    public class ClickEvent : EventBase { }
    public class GeometryChangedEvent : EventBase { }
    public class ChangeEvent<T> : EventBase { public T newValue; public T previousValue; }
    public delegate void EventCallback<in TEvent>(TEvent evt);

    public struct Rect2 { public float width, height; public float x, y; }

    /// <summary>Resolved layout/style readback. Nothing here is laid out, so
    /// these are inert defaults; Brink derives its column maths from
    ///  numbers rather than from resolved layout.</summary>
    public struct ResolvedStyle
    {
        public float width, height, fontSize, opacity;
        public float marginTop, marginBottom, marginLeft, marginRight;
        public float paddingTop, paddingBottom, paddingLeft, paddingRight;
        public float top, bottom, left, right;
        public float minWidth, minHeight, maxWidth, maxHeight;
        public float flexGrow, flexShrink, unityParagraphSpacing;
        public float borderLeftWidth, borderRightWidth, borderTopWidth, borderBottomWidth;
        public DisplayStyle display;
        public Visibility visibility;
        public object color, backgroundColor;
    }

    public interface IVisualElementScheduledItem
    {
        IVisualElementScheduledItem ExecuteLater(long ms);
        IVisualElementScheduledItem Every(long ms);
        IVisualElementScheduledItem StartingIn(long ms);
        void Pause();
        void Resume();
    }

    sealed class ScheduledItem : IVisualElementScheduledItem
    {
        public IVisualElementScheduledItem ExecuteLater(long ms) => this;
        public IVisualElementScheduledItem Every(long ms) => this;
        public IVisualElementScheduledItem StartingIn(long ms) => this;
        public void Pause() { }
        public void Resume() { }
    }

    public interface IVisualElementScheduler
    {
        IVisualElementScheduledItem Execute(Action a);
        IVisualElementScheduledItem Execute(Action<object> a);
    }

    sealed class Scheduler : IVisualElementScheduler
    {
        // Runs immediately: the harness has no frame loop, and every scheduled
        // callback in this project is a deferred measure or refresh whose
        // *effect* is what a test would look at.
        public IVisualElementScheduledItem Execute(Action a) { a?.Invoke(); return new ScheduledItem(); }
        public IVisualElementScheduledItem Execute(Action<object> a) { a?.Invoke(null); return new ScheduledItem(); }
    }

    public class VisualElement : IEnumerable<VisualElement>
    {
        readonly List<VisualElement> children = new List<VisualElement>();
        readonly List<string> classes = new List<string>();
        readonly Dictionary<Type, List<object>> callbacks = new Dictionary<Type, List<object>>();

        public string name { get; set; } = "";
        public IStyle style { get; } = new Style();
        public VisualElement parent { get; private set; }
        public PickingMode pickingMode { get; set; } = PickingMode.Position;
        public bool focusable { get; set; }
        public object userData { get; set; }
        public string tooltip { get; set; } = "";
        public string viewDataKey { get; set; } = "";
        public ResolvedStyle resolvedStyle => default;
        public IVisualElementScheduler schedule { get; } = new Scheduler();
        public enum MeasureMode { Undefined, Exactly, AtMost }
        public Vector2 MeasureTextSize(string text, float w, MeasureMode wm, float h, MeasureMode hm)
            => new Vector2((text ?? "").Length * 8f, 16f);
        public int IndexOf(VisualElement child) => children.IndexOf(child);
        public VisualElement ElementAt(int i) => children[i];
        public void SendToBack() { }
        public void BringToFront() { }

        public int childCount => children.Count;
        public IEnumerable<VisualElement> Children() => children;
        public VisualElement this[int i] => children[i];

        public VisualElement contentContainer => this;
        public Rect2 layout => new Rect2();
        public Rect2 contentRect => new Rect2();
        public Rect2 worldBound => new Rect2();
        public object panel => null;

        public void Add(VisualElement child)
        {
            if (child == null) return;
            child.parent = this;
            children.Add(child);
        }
        public void Insert(int index, VisualElement child)
        {
            if (child == null) return;
            child.parent = this;
            children.Insert(Math.Max(0, Math.Min(index, children.Count)), child);
        }
        public void Remove(VisualElement child)
        {
            if (child == null) return;
            children.Remove(child);
            child.parent = null;
        }
        public void RemoveAt(int i) { children[i].parent = null; children.RemoveAt(i); }
        public void Clear()
        {
            foreach (var c in children) c.parent = null;
            children.Clear();
        }
        public void RemoveFromHierarchy() { parent?.Remove(this); }
        public VisualElement hierarchy => this;

        public void AddToClassList(string c) { if (!string.IsNullOrEmpty(c) && !classes.Contains(c)) classes.Add(c); }
        public void RemoveFromClassList(string c) { classes.Remove(c); }
        public void ClearClassList() { classes.Clear(); }
        public bool ClassListContains(string c) => classes.Contains(c);
        public void EnableInClassList(string c, bool on) { if (on) AddToClassList(c); else RemoveFromClassList(c); }
        public IEnumerable<string> GetClasses() => classes;

        public virtual void SetEnabled(bool v) { enabledSelf = v; }
        public bool enabledSelf { get; private set; } = true;
        public bool enabledInHierarchy => enabledSelf && (parent == null || parent.enabledInHierarchy);

        public void RegisterCallback<TEvent>(EventCallback<TEvent> cb) where TEvent : EventBase
        {
            if (!callbacks.TryGetValue(typeof(TEvent), out var list))
                callbacks[typeof(TEvent)] = list = new List<object>();
            list.Add(cb);
        }
        public void UnregisterCallback<TEvent>(EventCallback<TEvent> cb) where TEvent : EventBase
        {
            if (callbacks.TryGetValue(typeof(TEvent), out var list)) list.Remove(cb);
        }
        public void Focus() { }
        public void MarkDirtyRepaint() { }

        /// <summary>Depth-first walk, used by the shim's Query support.</summary>
        public IEnumerable<VisualElement> Descendants()
        {
            foreach (var c in children)
            {
                yield return c;
                foreach (var d in c.Descendants()) yield return d;
            }
        }

        public UQueryBuilder<T> Query<T>(string name = null, string className = null) where T : VisualElement
            => new UQueryBuilder<T>(this, name, className);
        public UQueryBuilder<VisualElement> Query(string name = null, string className = null)
            => new UQueryBuilder<VisualElement>(this, name, className);
        public T Q<T>(string name = null, string className = null) where T : VisualElement
            => Query<T>(name, className).First();
        public VisualElement Q(string name = null, string className = null)
            => Query<VisualElement>(name, className).First();

        public IEnumerator<VisualElement> GetEnumerator() => children.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => children.GetEnumerator();
    }

    public class UQueryBuilder<T> where T : VisualElement
    {
        readonly VisualElement root; readonly string name; readonly string className;
        public UQueryBuilder(VisualElement root, string name, string className)
        { this.root = root; this.name = name; this.className = className; }

        IEnumerable<T> Matches() => root.Descendants().OfType<T>()
            .Where(e => (name == null || e.name == name)
                        && (className == null || e.ClassListContains(className)));

        public T First() => Matches().FirstOrDefault();
        public List<T> ToList() => Matches().ToList();
        public void ForEach(Action<T> a) { foreach (var e in Matches()) a(e); }
    }

    public class TextElement : VisualElement
    {
        public virtual string text { get; set; } = "";
    }

    public class Label : TextElement
    {
        public Label() { }
        public Label(string text) { this.text = text; }
    }

    public class Button : TextElement
    {
        public Action clicked;
        public Button() { }
        public Button(Action onClick) { clicked = onClick; }
        /// <summary>Harness-only: fire the click the way a tap would.</summary>
        public void SendClick() { if (enabledSelf) clicked?.Invoke(); }
    }

    public class Toggle : VisualElement
    {
        public string label { get; set; } = "";
        public bool value { get; set; }
        public Toggle() { }
        public Toggle(string label) { this.label = label; }
        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<bool>> cb) { }
        public void SetValueWithoutNotify(bool v) { value = v; }
    }

    public class Slider : VisualElement
    {
        public string label { get; set; } = "";
        public float value { get; set; }
        public float lowValue { get; set; }
        public float highValue { get; set; } = 1f;
        public Slider() { }
        public Slider(string label, float low, float high) { this.label = label; lowValue = low; highValue = high; }
        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<float>> cb) { }
        public void SetValueWithoutNotify(float v) { value = v; }
        public bool showInputField { get; set; }
        public SliderDirection direction { get; set; }
    }

    // Input fields used by StrategistView and OperationPlanningPanel. Absent
    // until now, which is why the runtime assembly would not build outside
    // Unity at all — the harness README's whole claim. Same shape as Toggle and
    // Slider above: enough surface for the views to compile and be walked, no
    // behaviour, because nothing in the suite drives them.
    public class TextField : VisualElement
    {
        public string label { get; set; } = "";
        public string value { get; set; } = "";
        public bool isReadOnly { get; set; }
        public bool multiline { get; set; }
        public TextField() { }
        public TextField(string label) { this.label = label; }
        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<string>> cb) { }
        public void SetValueWithoutNotify(string v) { value = v; }
    }

    public class FloatField : VisualElement
    {
        public string label { get; set; } = "";
        public float value { get; set; }
        public bool isReadOnly { get; set; }
        public FloatField() { }
        public FloatField(string label) { this.label = label; }
        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<float>> cb) { }
        public void SetValueWithoutNotify(float v) { value = v; }
    }

    public class DropdownField : VisualElement
    {
        public string label { get; set; } = "";
        public List<string> choices { get; set; } = new List<string>();
        public int index { get; set; }
        public string value { get; set; } = "";
        public DropdownField() { }
        public DropdownField(string label) { this.label = label; }
        public DropdownField(string label, List<string> choices, int defaultIndex)
        {
            this.label = label;
            this.choices = choices ?? new List<string>();
            index = defaultIndex;
            if (this.choices.Count > 0 && defaultIndex >= 0 && defaultIndex < this.choices.Count)
                value = this.choices[defaultIndex];
        }
        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<string>> cb) { }
        public void SetValueWithoutNotify(string v) { value = v; }
    }

    public enum ScrollerVisibility { Auto, AlwaysVisible, Hidden }

    public enum SliderDirection { Horizontal, Vertical }

    public class ScrollView : VisualElement
    {
        public ScrollerVisibility verticalScrollerVisibility { get; set; } = ScrollerVisibility.Auto;
        public ScrollerVisibility horizontalScrollerVisibility { get; set; } = ScrollerVisibility.Auto;
        public ScrollView() { }
        public ScrollView(ScrollViewMode mode) { }
        public VisualElement contentViewport { get; } = new VisualElement();
        public VisualElement verticalScroller { get; } = new VisualElement();
        public VisualElement horizontalScroller { get; } = new VisualElement();
        public ScrollViewMode mode { get; set; }
        public Vector2 scrollOffset { get; set; }
    }

    public class StyleSheet : UnityEngine.Object { }
    public class ThemeStyleSheet : StyleSheet { }
    public class VisualTreeAsset : UnityEngine.Object
    {
        public VisualElement Instantiate() => new VisualElement();
        public VisualElement CloneTree() => new VisualElement();
        public void CloneTree(VisualElement target) { }
    }
    public enum PanelScaleMode { ConstantPixelSize, ConstantPhysicalSize, ScaleWithScreenSize }

    public class PanelSettings : UnityEngine.ScriptableObject
    {
        public float scale { get; set; } = 1f;
        public ThemeStyleSheet themeStyleSheet { get; set; }
        public PanelScaleMode scaleMode { get; set; }
        public float referenceDpi { get; set; } = 96f;
        public Vector2 referenceResolution { get; set; }
        public float fallbackDpi { get; set; } = 96f;
        public bool clearColor { get; set; }
        public object targetTexture { get; set; }
    }
    public class UIDocument : MonoBehaviour
    {
        public VisualElement rootVisualElement { get; } = new VisualElement();
        public PanelSettings panelSettings { get; set; }
        public VisualTreeAsset visualTreeAsset { get; set; }
    }

}
