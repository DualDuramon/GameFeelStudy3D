using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    private static SpawnManager _instance;

    [SerializeField] private Transform[] _spawnPoints;
    [SerializeField] private GameObject[] _spawnPrefabs;

    public SpawnManager Instance
    {
        get
        {
            if(_instance == null)
            {
                _instance = FindFirstObjectByType<SpawnManager>();
            }
            return _instance;
        }

    }

    private void Awake()
    {
        if(_instance == null)
        {
            _instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void SpawnRandomObject()
    {
        int spawnPointIndex = Random.Range(0, _spawnPoints.Length);
        int prefabIndex = Random.Range(0, _spawnPrefabs.Length);

        Transform spawnPoint = _spawnPoints[spawnPointIndex];
        GameObject prefabToSpawn = _spawnPrefabs[prefabIndex];

        Instantiate(prefabToSpawn, spawnPoint.position, spawnPoint.rotation);
    }

    public void SpawnObject(int prefabIndex)
    {
        if (prefabIndex < 0 || prefabIndex >= _spawnPrefabs.Length)
        {
            Debug.LogError("Invalid prefab index.");
            return;
        }
        Transform spawnPoint = _spawnPoints[0];
        GameObject prefabToSpawn = _spawnPrefabs[prefabIndex];

        Instantiate(prefabToSpawn, spawnPoint.position, spawnPoint.rotation);
    }
}
