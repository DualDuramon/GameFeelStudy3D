using UnityEngine;
using System.Collections;

public class CameraShaker : MonoBehaviour
{
    private static CameraShaker _instance;
    
    [SerializeField] private Camera _camera;
    [SerializeField] private bool _canShake = true;
    private Coroutine _shakeRoutine;
    private Vector3 _originalPosition;

    public static CameraShaker Instance
    {
        get
        {
            if(_instance == null)
            {
                _instance = FindFirstObjectByType<CameraShaker>();
            }

            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        _originalPosition = transform.localPosition;
        _camera = Camera.main;
    }

    public void Shake(float duration, float strength)
    {
        if (!_canShake) return;

        if (_shakeRoutine != null)
        {
            StopCoroutine(_shakeRoutine);
        }

        InitOriginalPosition();
        _shakeRoutine = StartCoroutine(ShakeCoroutine(duration, strength));
    }

    private IEnumerator ShakeCoroutine(float duration, float strength)
    {
        _originalPosition = transform.localPosition;
        float elapsed = 0f;

        while(elapsed < duration)
        {
            Vector2 offset = Random.insideUnitCircle * strength;
            transform.localPosition = _originalPosition + new Vector3(offset.x, offset.y, 0f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = _originalPosition;
    }

    private void InitOriginalPosition()
    {
        if (_camera != null)
        {
            _originalPosition = _camera.transform.localPosition;
        }
    }

    public void ToggleCameraShake()
    {
        _canShake = !_canShake;
    }

}
