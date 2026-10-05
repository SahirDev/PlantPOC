using UnityEngine;

public class SpinObjects : MonoBehaviour
{
    [SerializeField]
    private float rotateSpeed = 200f;

    [SerializeField]
    private Vector3 spinDirection = Vector3.up;

    public bool shouldSpin = true;
    private Transform _transform;

    private void Awake()
    {
        _transform = transform;
    }

    private void Update()
    {
        if (shouldSpin)
        {
            _transform.Rotate(spinDirection, rotateSpeed * Time.deltaTime);
        }
    }
}