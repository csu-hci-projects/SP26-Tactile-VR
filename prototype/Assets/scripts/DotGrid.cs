using UnityEngine;


/// Procedurally generates a grid of dot GameObjects on the virtual whiteboard.
///
/// Dots are spawned at Start using the dotPrefab and arranged in a centered
/// grid based on the rows, cols, and spacing settings. Each dot is named
/// Dot_row_col for easy identification in the hierarchy.
///
/// The grid is positioned slightly in front of the whiteboard surface
/// (z offset of -0.1) so dots sit visibly on top of it rather than inside it.

public class DotGrid : MonoBehaviour
{
    [Header("Grid Settings")]
    // Number of rows in the dot grid
    public int rows = 3;
    // Number of columns in the dot grid
    public int cols = 3;
    // Distance in world units between each dot
    public float spacing = 0.3f;
    // The prefab to instantiate for each dot in the grid
    public GameObject dotPrefab;

    void Start()
    {
        GenerateGrid();
    }


    /// Instantiates a grid of dots centered on this GameObject's position.
    /// Each dot is placed in world space at a fixed z offset in front of
    /// the whiteboard, then parented to this transform for organization.
  
    void GenerateGrid()
    {
        // Calculate the starting x and y offsets so the grid is centered
        // on the whiteboard rather than starting from the top-left corner
        float startX = -((cols - 1) * spacing) / 2f;
        float startY = -((rows - 1) * spacing) / 2f;

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                // Compute this dot's local offset from the grid center
                float x = startX + col * spacing;
                float y = startY + row * spacing;

                // Build the world position directly from the parent transform's position
                // rather than using transform.TransformPoint, so the dot placement
                // ignores any scale applied to the whiteboard object
                Vector3 worldPos = new Vector3(
                    transform.position.x + x,
                    transform.position.y + y,
                    transform.position.z - 0.1f  // Slight z offset places dots in front of the whiteboard surface
                );

                // Spawn the dot at the calculated world position with no rotation
                GameObject dot = Instantiate(dotPrefab, worldPos, Quaternion.identity);

                // Parent the dot to this GameObject to keep the hierarchy tidy
                dot.transform.parent = transform;

                // Name each dot by its grid coordinates for easy identification
                dot.name = $"Dot_{row}_{col}";
            }
        }
    }
}