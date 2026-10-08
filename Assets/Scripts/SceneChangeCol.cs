using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Door / area that offers moving to another room.
/// React (through CommunicationManager):
///   handleSceneTriggerEntered { targetScene, text } -> show the prompt ("Go to Boiler Room?")
///   handleSceneTriggerExited  { targetScene, text } -> hide it
///   React's button then calls ChangeScene_Extern(targetScene).
/// The Y key still works as a shortcut.
/// </summary>
public class SceneChangeCol : MonoBehaviour
{
    public PlantScene selectScene;

    [TextArea]
    public string Text;

    [Tooltip("Keyboard shortcut to accept while inside the trigger.")]
    [SerializeField] private Key acceptKey = Key.Y;

    private bool canChangeScene;

    /// <summary>The door the worker is standing in (Unity prompt in the Plant UI), or null.</summary>
    public static SceneChangeCol Current { get; private set; }

    /// <summary>Readable room name of the target ("Boiler Room").</summary>
    public string TargetDisplayName
    {
        get
        {
            switch (selectScene)
            {
                case PlantScene.Main_Scene: return "Power Plant Area";
                case PlantScene.BoilerRoom: return "Boiler Room";
                case PlantScene.TurbineRoom: return "Turbine Room";
                case PlantScene.Control_Room: return "Control Room";
                default: return TargetSceneName;
            }
        }
    }

    private static readonly string[] SceneNames =
    {
        "Main_Scene",
        "BoilerRoom",
        "TurbineRoom",
        "Control_Room"
    };

    public string TargetSceneName
    {
        get
        {
            int index = (int)selectScene;
            return index >= 0 && index < SceneNames.Length ? SceneNames[index] : selectScene.ToString();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        canChangeScene = true;
        Current = this;
        CommunicationManager.HandleSceneTriggerEntered_Extern(new SceneTriggerPayload { targetScene = TargetSceneName, text = Text });
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        canChangeScene = false;
        if (Current == this) Current = null;
        CommunicationManager.HandleSceneTriggerExited_Extern(new SceneTriggerPayload { targetScene = TargetSceneName, text = Text });
    }

    private void OnDisable()
    {
        canChangeScene = false;
        if (Current == this) Current = null;
    }

    private void Update()
    {
        if (!canChangeScene) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[acceptKey].wasPressedThisFrame && !UITextInput.IsTyping) LoadScene();
    }

    public void LoadScene()
    {
        canChangeScene = false;
        if (Current == this) Current = null;
        CommunicationManager.HandleSceneTriggerExited_Extern(new SceneTriggerPayload { targetScene = TargetSceneName, text = Text });

        if (SceneController.Instance != null)
        {
            SceneController.Instance.ChangeScene(TargetSceneName);
        }
    }
}

/// <summary>Rooms of the plant. Renamed from "Scene" (which clashed with UnityEngine.SceneManagement.Scene).
/// The saved values in scenes stay the same.</summary>
public enum PlantScene
{
    Main_Scene,
    BoilerRoom,
    TurbineRoom,
    Control_Room
}
