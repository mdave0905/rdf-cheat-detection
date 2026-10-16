using UnityEngine;

/// <summary>
/// Constructs and maintains a clean 30m x 30m medieval testing courtyard arena.
/// Center: (0, 0, 0)
/// Boundaries: X in [-15, +15], Z in [-15, +15] (matching GameConstants.ArenaBoundaryX and Z).
/// </summary>
[ExecuteAlways]
public class ArenaEnvironment : MonoBehaviour
{
    [Header("Dimensions (Matches GameConstants)")]
    public float arenaWidth = 30f;
    public float arenaLength = 30f;
    public float wallHeight = 1.6f;
    public float wallThickness = 0.5f;

    [Header("Materials (Auto-resolved if empty)")]
    public Material groundMaterial;
    public Material wallMaterial;
    public Material curbMaterial;
    public Material centerEmblemMaterial;

    private const string ArenaRootName = "Generated_Courtyard_Arena";

    private void Awake()
    {
        EnsureArenaBuilt();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            EnsureArenaBuilt();
        }
    }
#endif

    public void EnsureArenaBuilt()
    {
        // Remove default scene floor tile if present before building the grid
        Transform legacyTile = transform.Find("SM_Env_FloorTile_01");
        if (legacyTile != null)
        {
            legacyTile.gameObject.SetActive(false);
        }

        // Center parent transform at world origin
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        // Check if generated arena already exists
        Transform existing = transform.Find(ArenaRootName);
        if (existing != null)
        {
            return; // Already configured
        }

        ResolveMaterials();

        GameObject arenaRoot = new GameObject(ArenaRootName);
        arenaRoot.transform.SetParent(transform, false);
        arenaRoot.transform.localPosition = Vector3.zero;

        BuildGround(arenaRoot.transform);
        BuildPerimeterWalls(arenaRoot.transform);
        BuildCornerPillars(arenaRoot.transform);
        BuildCenterEmblem(arenaRoot.transform);
    }

    private void ResolveMaterials()
    {
        // Find existing scene materials if not assigned
        if (groundMaterial == null || wallMaterial == null)
        {
            MeshRenderer[] renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            foreach (var r in renderers)
            {
                if (r.sharedMaterial != null && r.sharedMaterial.name.Contains("PolyAdventure"))
                {
                    if (groundMaterial == null) groundMaterial = r.sharedMaterial;
                    if (wallMaterial == null) wallMaterial = r.sharedMaterial;
                }
            }
        }

        // Fallback to standard shader if still null
        Shader urpShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        if (groundMaterial == null)
        {
            groundMaterial = new Material(urpShader) { name = "Arena_Ground_Mat" };
            groundMaterial.color = new Color(0.48f, 0.49f, 0.52f); // Clean flagstone grey
        }

        if (wallMaterial == null)
        {
            wallMaterial = new Material(urpShader) { name = "Arena_Wall_Mat" };
            wallMaterial.color = new Color(0.35f, 0.36f, 0.38f); // Castle stone dark grey
        }

        if (curbMaterial == null)
        {
            curbMaterial = new Material(urpShader) { name = "Arena_Curb_Mat" };
            curbMaterial.color = new Color(0.72f, 0.55f, 0.28f); // Subtle ornamental gold / stone trim
        }

        if (centerEmblemMaterial == null)
        {
            centerEmblemMaterial = new Material(urpShader) { name = "Arena_Center_Mat" };
            centerEmblemMaterial.color = new Color(0.20f, 0.35f, 0.55f); // Heraldic blue compass marker
        }
    }

    private void BuildGround(Transform parent)
    {
        GameObject groundObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        groundObj.name = "Courtyard_Floor_30x30";
        groundObj.transform.SetParent(parent, false);
        groundObj.transform.localPosition = new Vector3(0f, -0.1f, 0f);
        groundObj.transform.localScale = new Vector3(arenaWidth, 0.2f, arenaLength);

        MeshRenderer mr = groundObj.GetComponent<MeshRenderer>();
        if (mr != null && groundMaterial != null)
        {
            mr.sharedMaterial = groundMaterial;
        }

        // Subtle decorative border curb
        BuildCurbBorder(parent);
    }

    private void BuildCurbBorder(Transform parent)
    {
        float halfW = arenaWidth / 2f;
        float halfL = arenaLength / 2f;
        float curbWidth = 0.25f;
        float curbHeight = 0.04f;

        CreateCurbPiece(parent, "Curb_North", new Vector3(0f, curbHeight / 2f, halfL - 0.2f), new Vector3(arenaWidth, curbHeight, curbWidth));
        CreateCurbPiece(parent, "Curb_South", new Vector3(0f, curbHeight / 2f, -halfL + 0.2f), new Vector3(arenaWidth, curbHeight, curbWidth));
        CreateCurbPiece(parent, "Curb_East", new Vector3(halfW - 0.2f, curbHeight / 2f, 0f), new Vector3(curbWidth, curbHeight, arenaLength));
        CreateCurbPiece(parent, "Curb_West", new Vector3(-halfW + 0.2f, curbHeight / 2f, 0f), new Vector3(curbWidth, curbHeight, arenaLength));
    }

    private void CreateCurbPiece(Transform parent, string name, Vector3 pos, Vector3 scale)
    {
        GameObject curb = GameObject.CreatePrimitive(PrimitiveType.Cube);
        curb.name = name;
        curb.transform.SetParent(parent, false);
        curb.transform.localPosition = pos;
        curb.transform.localScale = scale;

        Collider col = curb.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);

        MeshRenderer mr = curb.GetComponent<MeshRenderer>();
        if (mr != null && curbMaterial != null) mr.sharedMaterial = curbMaterial;
    }

    private void BuildPerimeterWalls(Transform parent)
    {
        float halfW = arenaWidth / 2f;
        float halfL = arenaLength / 2f;
        float yPos = wallHeight / 2f;

        // Outer perimeter boundary walls placed right at ±15m
        CreateWall(parent, "Wall_North", new Vector3(0f, yPos, halfL + wallThickness / 2f), new Vector3(arenaWidth + wallThickness * 2f, wallHeight, wallThickness));
        CreateWall(parent, "Wall_South", new Vector3(0f, yPos, -halfL - wallThickness / 2f), new Vector3(arenaWidth + wallThickness * 2f, wallHeight, wallThickness));
        CreateWall(parent, "Wall_East", new Vector3(halfW + wallThickness / 2f, yPos, 0f), new Vector3(wallThickness, wallHeight, arenaLength));
        CreateWall(parent, "Wall_West", new Vector3(-halfW - wallThickness / 2f, yPos, 0f), new Vector3(wallThickness, wallHeight, arenaLength));
    }

    private void CreateWall(Transform parent, string name, Vector3 pos, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = pos;
        wall.transform.localScale = scale;

        MeshRenderer mr = wall.GetComponent<MeshRenderer>();
        if (mr != null && wallMaterial != null)
        {
            mr.sharedMaterial = wallMaterial;
        }

        // Wall cap coping for a clean architectural finish
        GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cap.name = $"{name}_Coping";
        cap.transform.SetParent(wall.transform, false);
        cap.transform.localPosition = new Vector3(0f, 0.52f, 0f);
        cap.transform.localScale = new Vector3(1.04f, 0.08f, 1.25f);
        
        Collider capCol = cap.GetComponent<Collider>();
        if (capCol != null) DestroyImmediate(capCol);

        MeshRenderer capMr = cap.GetComponent<MeshRenderer>();
        if (capMr != null && curbMaterial != null) capMr.sharedMaterial = curbMaterial;
    }

    private void BuildCornerPillars(Transform parent)
    {
        float halfW = arenaWidth / 2f;
        float halfL = arenaLength / 2f;
        float pillarHeight = wallHeight + 0.6f;
        float pillarSize = 1.1f;

        Vector3[] cornerPositions = new Vector3[]
        {
            new Vector3(-halfW, pillarHeight / 2f, -halfL),
            new Vector3(-halfW, pillarHeight / 2f, halfL),
            new Vector3(halfW, pillarHeight / 2f, -halfL),
            new Vector3(halfW, pillarHeight / 2f, halfL),
            // Midpoint gate posts
            new Vector3(0f, (pillarHeight - 0.2f) / 2f, halfL),
            new Vector3(0f, (pillarHeight - 0.2f) / 2f, -halfL),
            new Vector3(halfW, (pillarHeight - 0.2f) / 2f, 0f),
            new Vector3(-halfW, (pillarHeight - 0.2f) / 2f, 0f)
        };

        for (int i = 0; i < cornerPositions.Length; i++)
        {
            string pName = i < 4 ? $"Corner_Bastion_Pillar_0{i + 1}" : $"Mid_Gateway_Pillar_0{i - 3}";
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.name = pName;
            pillar.transform.SetParent(parent, false);
            pillar.transform.localPosition = cornerPositions[i];
            pillar.transform.localScale = new Vector3(pillarSize, pillarHeight, pillarSize);

            MeshRenderer mr = pillar.GetComponent<MeshRenderer>();
            if (mr != null && wallMaterial != null) mr.sharedMaterial = wallMaterial;

            // Pillar top pyramidal/beveled cap
            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.name = "Pillar_Cap";
            cap.transform.SetParent(pillar.transform, false);
            cap.transform.localPosition = new Vector3(0f, 0.52f, 0f);
            cap.transform.localScale = new Vector3(1.15f, 0.12f, 1.15f);

            Collider capCol = cap.GetComponent<Collider>();
            if (capCol != null) DestroyImmediate(capCol);

            MeshRenderer capMr = cap.GetComponent<MeshRenderer>();
            if (capMr != null && curbMaterial != null) capMr.sharedMaterial = curbMaterial;
        }
    }

    private void BuildCenterEmblem(Transform parent)
    {
        // Central courtyard origin medallion at (0, 0, 0)
        GameObject emblem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        emblem.name = "Courtyard_Center_Medallion";
        emblem.transform.SetParent(parent, false);
        emblem.transform.localPosition = new Vector3(0f, 0.015f, 0f);
        emblem.transform.localScale = new Vector3(3.5f, 0.02f, 3.5f);

        Collider col = emblem.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);

        MeshRenderer mr = emblem.GetComponent<MeshRenderer>();
        if (mr != null && centerEmblemMaterial != null) mr.sharedMaterial = centerEmblemMaterial;

        // Inner circle ring
        GameObject inner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        inner.name = "Center_Inner_Ring";
        inner.transform.SetParent(emblem.transform, false);
        inner.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        inner.transform.localScale = new Vector3(0.65f, 0.5f, 0.65f);

        Collider innerCol = inner.GetComponent<Collider>();
        if (innerCol != null) DestroyImmediate(innerCol);

        MeshRenderer innerMr = inner.GetComponent<MeshRenderer>();
        if (innerMr != null && curbMaterial != null) innerMr.sharedMaterial = curbMaterial;
    }
}
