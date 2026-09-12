using UnityEngine;

public class PostEffectExecuter : MonoBehaviour
{
[Header("CameraShaker")]
    [SerializeField] private CameraShakeSignalSender _cameraShakeSignalSender;
    [SerializeField] private float _shakeDuration = 0.5f;
    [SerializeField] private float _shakeStrength = 1.0f;
[Header("PostEffects")]
    [SerializeField] private GameObject _hitEffectPrefab;


    public void ExecutePostEffects(Vector3 position, Quaternion rotation, bool shakeCamera = true)
    {
        if (shakeCamera)
        {
            ShakeCamera(_shakeDuration, _shakeStrength);
        }

        if (_hitEffectPrefab != null)
        {
            Instantiate(_hitEffectPrefab, position, rotation);
        }
    }

    public void ShakeCamera(float duration, float strength)
    {
        if (_cameraShakeSignalSender != null)
        {
            _cameraShakeSignalSender.SendShakeSignal();
        }
    }
}
