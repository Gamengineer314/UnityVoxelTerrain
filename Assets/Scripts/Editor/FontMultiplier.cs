#if UNITY_EDITOR_LINUX
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;


// Modified from https://github.com/5argon/ModifyEditorStyle and https://gist.github.com/nukadelic/47474c7e5d4ee5909462e3b900f4cb82

[InitializeOnLoad]
public class FontMultiplier {
    private static readonly Dictionary<string, DefaultSize> defaultValues;
    private static Delegate prevDelegate;
    private static Delegate newDelegate;

    private static float Multiplier {
        get => EditorPrefs.GetFloat("FontMultiplier_Multiplier", 1f);
        set => EditorPrefs.SetFloat("FontMultiplier_Multiplier", value);
    }

    private static bool Enabled {
        get => EditorPrefs.GetBool("FontMultiplier_Enabled", false);
        set {
            EditorPrefs.SetBool("FontMultiplier_Enabled", value);
            ChangeDelegate();
        }
    }

    static FontMultiplier() {
        defaultValues = DefaultSize.Deserialize(
            SessionState.GetString("FontMultiplier_DefaultValues", "{}")
        );
        EditorApplication.hierarchyWindowItemOnGUI -= ModifyStartUp;
        EditorApplication.hierarchyWindowItemOnGUI += ModifyStartUp;
    }

    [SettingsProvider]
    private static SettingsProvider FontMultiplierSettingsProvider()
        => new FontMultiplierProvider("Preferences/Font Multiplier");

    private class FontMultiplierProvider : SettingsProvider {
        public FontMultiplierProvider(string path, SettingsScope scopes = SettingsScope.User)
        : base(path, scopes) { }

        public override void OnGUI(string searchContext)
            => FontMultiplierPreference();
    }

    private static void FontMultiplierPreference() {
        Multiplier = EditorGUILayout.FloatField("Font Multiplier", Multiplier);
        Enabled = EditorGUILayout.ToggleLeft("Enabled", Enabled);
    }

    private static void ModifyStartUp(int instanceID, Rect selectionRect) {
        EditorApplication.hierarchyWindowItemOnGUI -= ModifyStartUp;
        ChangeDelegate();
    }


    private static void ChangeDelegate() {
        FieldInfo onDrawField = typeof(GUIStyle).GetField("onDraw", BindingFlags.Static | BindingFlags.NonPublic);
        Type drawHandler = onDrawField.FieldType;
        Type drawStates = drawHandler.GetMethod("Invoke").GetParameters()[3].ParameterType;
        Delegate currentDelegate = (Delegate)onDrawField.GetValue(null);
        if (currentDelegate != newDelegate && Enabled) { // Add the delegate to modify GUIStyle
            prevDelegate = currentDelegate;
            newDelegate = Delegate.CreateDelegate(
                drawHandler,
                typeof(FontMultiplier)
                    .GetMethod("UpdateSizeOnDraw", BindingFlags.Static | BindingFlags.NonPublic)
                    .MakeGenericMethod(new Type[] { drawStates })
            );
            onDrawField.SetValue(null, newDelegate);
        }
        else if (currentDelegate == newDelegate && !Enabled) { // Remove the delegate
            onDrawField.SetValue(null, prevDelegate);
            EditorUtility.RequestScriptReload();
        }
    }

    // DrawStates is internal but we can use a generic method
    private static bool UpdateSizeOnDraw<DrawStates>(GUIStyle style, Rect rect, GUIContent content, DrawStates states) {
        if (!defaultValues.ContainsKey(style.name)) { // Store default size
            defaultValues[style.name] = new(style);
            SessionState.SetString("FontMultiplier_DefaultValues", DefaultSize.Serialize(defaultValues));
        }
        defaultValues[style.name].Modify(style); // Update size
        return (bool)prevDelegate.Method.Invoke(prevDelegate.Target, new object[] { style, rect, content, states });
    }


    private struct DefaultSize {
        public int font, top, bottom, left, right;

        public DefaultSize(GUIStyle style) {
            font = style.fontSize;
            top = style.padding.top;
            bottom = style.padding.bottom;
            left = style.padding.left;
            right = style.padding.right;
        }

        public readonly void Modify(GUIStyle style) {
            style.fontSize = (int)Math.Ceiling(font * Multiplier);

            // Seems to be worse :(
            //style.padding.top = (int)Math.Ceiling(top * Multiplier);
            //style.padding.bottom = (int)Math.Ceiling(bottom * Multiplier);
            //style.padding.left = (int)Math.Ceiling(left * Multiplier);
            //style.padding.right = (int)Math.Ceiling(right * Multiplier);

            // Seems a little better :)
            if (style.padding.bottom < 3) style.padding.bottom = 0;
            if (style.padding.top < 3) style.padding.top = 0;
        }

        // Serialization
        public override readonly string ToString() => $"{font} {top} {bottom} {left} {right}";

        private DefaultSize(string s) {
            string[] values = s.Split(' ');
            font = int.Parse(values[0]);
            top = int.Parse(values[1]);
            bottom = int.Parse(values[2]);
            left = int.Parse(values[3]);
            right = int.Parse(values[4]);
        }

        public static string Serialize(Dictionary<string, DefaultSize> dict) {
            string s = "";
            foreach (KeyValuePair<string, DefaultSize> kv in dict) {
                s += kv.Key + "|" + kv.Value + "\n";
            }
            return s;
        }

        public static Dictionary<string, DefaultSize> Deserialize(string s) {
            Dictionary<string, DefaultSize> dict = new();
            foreach (string line in s.Split('\n')) {
                int i = line.IndexOf('|');
                if (i != -1) dict[line[..i]] = new(line[(i + 1)..]);
            }
            return dict;
        }
    }
}
#endif