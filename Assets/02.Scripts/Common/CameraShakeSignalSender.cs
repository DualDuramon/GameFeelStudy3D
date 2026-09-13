using UnityEngine;

public class CameraShakeSignalSender : MonoBehaviour
{
    [SerializeField] private float _shakeDuration = 0.2f;
    [SerializeField] private float _shakeStrength = 0.5f;

    public void SendShakeSignal()
    {
        CameraShaker.Instance.Shake(_shakeDuration, _shakeStrength);
    }
    
    public void SendShakeSignal(float duration, float strength)
    {
        CameraShaker.Instance.Shake(duration, strength);
    }
}
