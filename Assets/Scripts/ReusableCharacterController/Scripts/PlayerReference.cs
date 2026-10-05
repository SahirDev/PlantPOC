using System;
using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "PlayerReference", menuName = "Character Controller/Player Reference")]
public sealed class PlayerReference : ScriptableObject
{
    [Serializable] public sealed class PlayerEvent : UnityEvent<Player> { }
    [SerializeField] private PlayerEvent onPlayerAvailable = new PlayerEvent();
    [SerializeField] private UnityEvent onPlayerUnavailable = new UnityEvent();
    [NonSerialized] private Player current;

    public Player Current => current;
    public bool HasPlayer => current != null;
    public PlayerEvent OnPlayerAvailable => onPlayerAvailable;
    public UnityEvent OnPlayerUnavailable => onPlayerUnavailable;

    internal void Register(Player player)
    {
        if (current == player) return;
        if (current != null)
        {
            Debug.LogError("This PlayerReference is already registered to another live player.", this);
            return;
        }
        current = player;
        onPlayerAvailable.Invoke(player);
    }

    internal void Unregister(Player player)
    {
        if (current != player) return;
        current = null;
        onPlayerUnavailable.Invoke();
    }

    // These forwarding methods can also be assigned to Inspector UnityEvents.
    public void SuspendCharacterControl() { if (current != null) current.SuspendCharacterControl(); }
    public void ResumeCharacterControl() { if (current != null) current.ResumeCharacterControl(); }
    public void Focus(GameObject target) { if (current != null) current.FocusController.Focus(target); }
    public void ExitFocus() { if (current != null) current.FocusController.ExitFocus(); }
    public void LoadScene(string sceneNameOrPath) { if (current != null) current.LoadScene(sceneNameOrPath); }
    public void LoadScene(int sceneIndex) { if (current != null) current.LoadScene(sceneIndex); }
}
