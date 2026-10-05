using System;
using UnityEngine;

public class ControlRoomTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Player") && HUDController.Instance != null)
        {
            HUDController.Instance.SetInRangeOfEquipment(EquipmentType.ControlRoom, gameObject);
        }
    }

    // Was missing: without it React would think the worker is at the control room forever.
    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Player") && HUDController.Instance != null)
        {
            HUDController.Instance.ResetInRangeOfEquipment(EquipmentType.ControlRoom, gameObject);
        }
    }
}