using UnityEngine;

public class BoilerTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && HUDController.Instance != null)
        {
            HUDController.Instance.SetInRangeOfEquipment(EquipmentType.Boiler, gameObject);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") && HUDController.Instance != null)
        {
            HUDController.Instance.ResetInRangeOfEquipment(EquipmentType.Boiler, gameObject);
        }
    }
}
