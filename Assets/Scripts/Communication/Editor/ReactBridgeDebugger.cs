using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > React Bridge Debugger. In Play Mode: call any CommunicationManager _Extern method exactly as
/// React does (sendMessage("CommunicationManager", method, value)) and see every handleXxx Unity sends back.
/// Lets the Unity side be tested without React or a WebGL build.
/// </summary>
public class ReactBridgeDebugger : EditorWindow
{
    private const int MaxLogLines = 300;

    // Example values for methods that take a parameter.
    private static readonly Dictionary<string, string> ExampleValues = new Dictionary<string, string>
    {
        { "ChangeScene_Extern", "BoilerRoom" },
        { "GetSceneDownloadSize_Extern", "BoilerRoom" },
        { "PreloadScene_Extern", "TurbineRoom" },
        { "SetCameraMode_Extern", "worker" },
        { "SetCursorLocked_Extern", "false" },
        { "SetPointerOverUI_Extern", "false" },
        { "SetKeyboardCapture_Extern", "false" },
        { "SetWorkerTracking_Extern", "true/0.5" },
        { "SetVolume_Extern", "1" },
        { "SetMiniMapVisible_Extern", "false" },
        { "SetViewMode_Extern", "fpp" },
        { "SetSteamPressure_Extern", "70" },
        { "GetTurbineData_Extern", "1" },
        { "SetBurnerPower_Extern", "80" },
        { "SetValveOpening_Extern", "30" },
        { "SetGeneratorValue_Extern", "92" },
    };

    private static readonly HashSet<string> StreamEvents = new HashSet<string>
    {
        "handleBoilerDashboard", "handleWorkerTransform", "handleTurbineData", "handleSceneDownloadProgress"
    };

    private readonly List<string> log = new List<string>();
    private string[] methodNames;
    private bool[] methodHasParameter;
    private int selected;
    private string value = "";
    private Vector2 scroll;
    private bool showStreams = true;

    [MenuItem("Tools/React Bridge Debugger")]
    private static void Open()
    {
        GetWindow<ReactBridgeDebugger>("React Bridge");
    }

    private void OnEnable()
    {
        CommunicationManager.MessageSent += OnSent;
        CollectMethods();
    }

    private void OnDisable()
    {
        CommunicationManager.MessageSent -= OnSent;
    }

    // Every public instance method ending in _Extern = everything React can call.
    private void CollectMethods()
    {
        var names = new List<string>();
        var hasParameter = new List<bool>();

        foreach (MethodInfo method in typeof(CommunicationManager).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (!method.Name.EndsWith("_Extern")) continue;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length > 1 || (parameters.Length == 1 && parameters[0].ParameterType != typeof(string))) continue;

            names.Add(method.Name);
            hasParameter.Add(parameters.Length == 1);
        }

        methodNames = names.ToArray();
        methodHasParameter = hasParameter.ToArray();
    }

    private void OnSent(string jsFunction, string data)
    {
        if (!showStreams && StreamEvents.Contains(jsFunction)) return;
        AddLog($"Unity -> React  {jsFunction}({data})");
    }

    private void AddLog(string line)
    {
        log.Add(line);
        if (log.Count > MaxLogLines) log.RemoveAt(0);
        scroll.y = float.MaxValue;
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Call Unity as React does", EditorStyles.boldLabel);

        if (methodNames == null || methodNames.Length == 0) CollectMethods();

        EditorGUI.BeginChangeCheck();
        selected = EditorGUILayout.Popup("Method", selected, methodNames);
        if (EditorGUI.EndChangeCheck())
            value = ExampleValues.TryGetValue(methodNames[selected], out string example) ? example : "";

        bool needsValue = methodHasParameter[selected];
        using (new EditorGUI.DisabledScope(!needsValue)) value = EditorGUILayout.TextField("Value", needsValue ? value : "");

        using (new EditorGUI.DisabledScope(!Application.isPlaying || !CommunicationManager.HasInstance))
        {
            if (GUILayout.Button("Send"))
            {
                string method = methodNames[selected];
                AddLog($"React -> Unity  {method}({(needsValue ? value : "")})");

                if (needsValue) CommunicationManager.Instance.gameObject.SendMessage(method, value);
                else CommunicationManager.Instance.gameObject.SendMessage(method);
            }
        }

        if (!Application.isPlaying) EditorGUILayout.HelpBox("Enter Play Mode to send.", MessageType.Info);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Messages", EditorStyles.boldLabel);
            showStreams = GUILayout.Toggle(showStreams, "Show live data streams", GUILayout.Width(170));
            if (GUILayout.Button("Clear", GUILayout.Width(60))) log.Clear();
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (string line in log) EditorGUILayout.SelectableLabel(line, GUILayout.Height(18));
        EditorGUILayout.EndScrollView();
    }
}
