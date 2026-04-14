using UnityEngine;

public class DotGrid : MonoBehaviour
{
    [Header("Grid Settings")]
    public int rows = 3;
    public int cols = 3;
    public float spacing = 0.3f;
    public GameObject dotPrefab;

    void Start()
    {
        GenerateGrid();
    }

    void GenerateGrid()
    {
        float startX = -((cols - 1) * spacing) / 2f;
        float startY = -((rows - 1) * spacing) / 2f;

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                float x = startX + col * spacing;
                float y = startY + row * spacing;

                // Place dots at whiteboard world position, ignoring scale
                Vector3 worldPos = new Vector3(
                    transform.position.x + x,
                    transform.position.y + y,
                    transform.position.z - 0.1f
                );

                GameObject dot = Instantiate(dotPrefab, worldPos, Quaternion.identity);
                dot.transform.parent = transform;
                dot.name = $"Dot_{row}_{col}";
            }
        }
    }
}