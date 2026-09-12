using UnityEngine;
using System.Collections;

public class CameraShaker : MonoBehaviour
{
    private static CameraShaker _instance;
    
    [SerializeField] private Camera _camera;
    private Coroutine _shakeRoutine;

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

        _camera = Camera.main;
    }

    public void Shake(float duration, float strength)
    {
        if (_shakeRoutine != null)
        {
            StopCoroutine(_shakeRoutine);
        }

        _shakeRoutine = StartCoroutine(ShakeCoroutine(duration, strength));
    }

    private IEnumerator ShakeCoroutine(float duration, float strength)
    {
        Vector3 originalPosition = transform.localPosition;
        float elapsed = 0f;

        while(elapsed < duration)
        {
            Vector2 offset = Random.insideUnitCircle * strength;
            transform.localPosition = originalPosition + new Vector3(offset.x, offset.y, 0f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = originalPosition;
    }

}
