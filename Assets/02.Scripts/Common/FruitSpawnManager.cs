using RuntimeMeshSlicing;
using UnityEngine;

public class FruitSpawnManager : MonoBehaviour
{
    private static FruitSpawnManager _instance;

    [SerializeField] private Transform[] _spawnPoints;
    [SerializeField] private GameObject[] _spawnPrefabs;

    public FruitSpawnManager Instance
    {
        get
        {
            if(_instance == null)
            {
                _instance = FindFirstObjectByType<FruitSpawnManager>();
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

    public void ToggleGeneratePiece()
    {
        SliceableSphere.GeneratePiece = !SliceableSphere.GeneratePiece;
    }

    public void ToggleGenerateJuices()
    {
        SliceableSphere.GenerateJuices = !SliceableSphere.GenerateJuices;
    }

    public void ClearAllFruits()
    {

        GameObject[] fruits = GameObject.FindGameObjectsWithTag("Fruit");
        foreach (var fruit in fruits)
        {
            Destroy(fruit);
        }
    }
}
