using UnityEditor;
using UnityEngine;

/// <summary>
/// Simple and intuitive Editor for SplineParticleFlow.
/// - Full Scene View Move (W) and Rotate (E) tool support.
/// - Simple sliders for Speed, Density, and Size.
/// - One-click buttons to Add Points in between or at the end.
/// </summary>
[CustomEditor(typeof(SplineParticleFlow))]
public class SplineParticleFlowEditor : Editor
{
    private SplineParticleFlow flow;
    private int selectedPointIndex = 0;

    private void OnEnable()
    {
        flow = (SplineParticleFlow)target;
        flow.EnsurePoints();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPresetButtons();

        EditorGUILayout.Space(8);
        DrawAppearanceControls();

        EditorGUILayout.Space(8);
        DrawMotionControls();

        EditorGUILayout.Space(8);
        DrawPointsControls();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawPresetButtons()
    {
        EditorGUILayout.LabelField("Quick Presets", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.2f, 0.9f, 1f);
        if (GUILayout.Button("⚡ Electric Current", GUILayout.Height(30)))
        {
            Undo.RecordObject(flow, "Set Electric Preset");
            flow.SetElectricPreset();
            EditorUtility.SetDirty(flow);
        }

        GUI.backgroundColor = new Color(0.88f, 0.95f, 1f);
        if (GUILayout.Button("💨 Steam Flow", GUILayout.Height(30)))
        {
            Undo.RecordObject(flow, "Set Steam Preset");
            flow.SetSteamPreset();
            EditorUtility.SetDirty(flow);
        }

        GUI.backgroundColor = new Color(0.2f, 1f, 0.85f);
        if (GUILayout.Button("➡ Energy Arrows", GUILayout.Height(30)))
        {
            Undo.RecordObject(flow, "Set Energy Arrows Preset");
            flow.SetEnergyArrowsPreset();
            EditorUtility.SetDirty(flow);
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawAppearanceControls()
    {
        EditorGUILayout.LabelField("1. Appearance & Size", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUI.BeginChangeCheck();

        var texProp = serializedObject.FindProperty("flowTexture");
        EditorGUILayout.PropertyField(texProp, new GUIContent("Texture"));

        var colorProp = serializedObject.FindProperty("color");
        EditorGUILayout.PropertyField(colorProp, new GUIContent("Color / Glow"));

        var sizeProp = serializedObject.FindProperty("size");
        EditorGUILayout.Slider(sizeProp, 0.05f, 3.0f, new GUIContent("Size (Width)"));

        var smoothProp = serializedObject.FindProperty("curveSmoothness");
        EditorGUILayout.IntSlider(smoothProp, 1, 30, new GUIContent("Curve Smoothness"));

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            flow.UpdateMaterialProperties();
            flow.UpdateLineGeometry();
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawMotionControls()
    {
        EditorGUILayout.LabelField("2. Movement & Density", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUI.BeginChangeCheck();

        var speedProp = serializedObject.FindProperty("speed");
        EditorGUILayout.Slider(speedProp, -15.0f, 15.0f, new GUIContent("Speed (Movement)"));

        var densityProp = serializedObject.FindProperty("density");
        EditorGUILayout.Slider(densityProp, 0.5f, 25.0f, new GUIContent("Density (Repeat)"));

        var reverseProp = serializedObject.FindProperty("reverse");
        EditorGUILayout.PropertyField(reverseProp, new GUIContent("Reverse Direction"));

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            flow.UpdateMaterialProperties();
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawPointsControls()
    {
        EditorGUILayout.LabelField("3. Spline Points (Move & Rotate in Scene)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.HelpBox("💡 Tip: Click any point below or in the Scene View. Then use Unity's Move tool (W) or Rotate tool (E) to adjust position and curve bend!", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
        if (GUILayout.Button("➕ Add Point in Between", GUILayout.Height(28)))
        {
            Undo.RecordObject(flow, "Add Point in Between");
            Transform newPoint = flow.AddPointInBetween(selectedPointIndex);
            if (newPoint != null)
            {
                Selection.activeTransform = newPoint;
                selectedPointIndex = flow.Points.IndexOf(newPoint);
            }
            EditorUtility.SetDirty(flow);
        }

        GUI.backgroundColor = new Color(0.3f, 0.8f, 1f);
        if (GUILayout.Button("➕ Add Point at End", GUILayout.Height(28)))
        {
            Undo.RecordObject(flow, "Add Point at End");
            Transform newPoint = flow.AddPointAtEnd();
            if (newPoint != null)
            {
                Selection.activeTransform = newPoint;
                selectedPointIndex = flow.Points.Count - 1;
            }
            EditorUtility.SetDirty(flow);
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        // List of points
        for (int i = 0; i < flow.Points.Count; i++)
        {
            var p = flow.Points[i];
            if (p == null) continue;

            bool isStart = (i == 0);
            bool isEnd = (i == flow.Points.Count - 1);
            bool isSelected = (selectedPointIndex == i);

            string title;
            Color capColor;
            if (isStart)
            {
                title = "🟢 [0] Start Point";
                capColor = new Color(0.3f, 0.9f, 0.4f);
            }
            else if (isEnd)
            {
                title = $"🔴 [{i}] End Point";
                capColor = new Color(1f, 0.4f, 0.4f);
            }
            else
            {
                title = $"🟡 [{i}] Bend Point";
                capColor = new Color(1f, 0.9f, 0.3f);
            }

            GUI.backgroundColor = isSelected ? new Color(0.3f, 0.85f, 1f) : capColor;
            EditorGUILayout.BeginHorizontal("box");
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button(title, EditorStyles.boldLabel))
            {
                selectedPointIndex = i;
                Selection.activeTransform = p;
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Select (W/E)", GUILayout.Width(95), GUILayout.Height(20)))
            {
                selectedPointIndex = i;
                Selection.activeTransform = p;
                SceneView.RepaintAll();
            }

            // Delete button for in-between points
            if (flow.Points.Count > 2 && !isStart && !isEnd)
            {
                GUI.backgroundColor = new Color(1f, 0.45f, 0.45f);
                if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(20)))
                {
                    Undo.RecordObject(flow, "Remove Point");
                    flow.RemovePoint(i);
                    selectedPointIndex = Mathf.Clamp(selectedPointIndex, 0, flow.Points.Count - 1);
                    EditorUtility.SetDirty(flow);
                    break;
                }
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    private void OnSceneGUI()
    {
        if (flow == null || flow.Points == null || flow.Points.Count == 0) return;

        for (int i = 0; i < flow.Points.Count; i++)
        {
            var p = flow.Points[i];
            if (p == null) continue;

            Vector3 worldPos = p.position;
            Quaternion worldRot = p.rotation;
            float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.16f;

            Color capColor = (i == 0) ? Color.green : (i == flow.Points.Count - 1) ? Color.red : Color.yellow;
            Handles.color = capColor;

            // Clickable Sphere Cap in Scene view
            if (Handles.Button(worldPos, Quaternion.identity, handleSize, handleSize * 1.3f, Handles.SphereHandleCap))
            {
                selectedPointIndex = i;
                Selection.activeTransform = p;
                Repaint();
            }

            // Point Label
            string label = (i == 0) ? "🟢 Start" : (i == flow.Points.Count - 1) ? "🔴 End" : $"🟡 Bend {i}";
            GUIStyle labelStyle = new GUIStyle();
            labelStyle.normal.textColor = capColor;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.fontSize = 12;
            Handles.Label(worldPos + Vector3.up * (handleSize * 1.6f), label, labelStyle);

            // Draw Tangent / Rotation direction line
            Vector3 forward = worldRot * Vector3.forward;
            Handles.color = new Color(1f, 1f, 0.2f, 0.8f);
            Handles.DrawLine(worldPos, worldPos + forward * (flow.Size * 2.0f));

            // If this point is selected, display Unity's current active tool (Move or Rotate)
            if (selectedPointIndex == i)
            {
                // Move Tool
                if (Tools.current == Tool.Move || Tools.current == Tool.None)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 newPos = Handles.PositionHandle(worldPos, Tools.pivotRotation == PivotRotation.Local ? worldRot : Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(p, "Move Spline Point");
                        p.position = newPos;
                        flow.UpdateLineGeometry();
                        EditorUtility.SetDirty(flow);
                    }
                }
                // Rotate Tool
                else if (Tools.current == Tool.Rotate)
                {
                    EditorGUI.BeginChangeCheck();
                    Quaternion newRot = Handles.RotationHandle(worldRot, worldPos);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(p, "Rotate Spline Point");
                        p.rotation = newRot;
                        flow.UpdateLineGeometry();
                        EditorUtility.SetDirty(flow);
                    }
                }
            }
        }
    }
}
