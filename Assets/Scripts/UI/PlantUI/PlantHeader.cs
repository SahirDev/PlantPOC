using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top header bar of the Plant UI (44 CSS px): HELIOS logo, title "Thermal Power Plant DIGITAL TWIN", Logout.
/// Logout sends handleLogout to React (React signs the user out). PlantUI keeps the bar at the top and starts all
/// other panels below it. Built by Tools > Thermal Plant > 18; restyle freely, keep the references.
/// </summary>
public class PlantHeader : MonoBehaviour
{
    [SerializeField] private TMP_Text title;
    [SerializeField] private Button logoutButton;
    [SerializeField] private string titleText = "Thermal Power Plant DIGITAL TWIN";

    private void Awake()
    {
        if (title != null && !string.IsNullOrEmpty(titleText)) title.text = titleText;
        if (logoutButton != null) logoutButton.onClick.AddListener(Logout);
    }

    public void Logout()
    {
        CommunicationManager.HandleLogout_Extern();
    }
}
