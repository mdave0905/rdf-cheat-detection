using UnityEngine;

/// <summary>
/// Visualizes the Server's authoritative position in real time.
/// Renders a holographic / ghost avatar at ServerAuthoritativeLoop.Instance.AuthoritativePosition,
/// and draws a line between the client and the server ghost. Because the server is one network trip behind,
/// the gap is mostly latency lag, so it is shown in a neutral colour.
/// </summary>
public class ServerGhostVisualizer : MonoBehaviour
{
    public static ServerGhostVisualizer Instance { get; private set; }

    [Header("Configuration")]
    [Tooltip("Whether the server ghost avatar is currently visible.")]
    public bool isGhostVisible = true;

    [Tooltip("Optional 3D avatar prefab (e.g. Character_Knight_White). If null, a stylized holographic avatar is generated automatically.")]
    public GameObject customGhostPrefab;

    [Header("Client-to-server line")]
    [Tooltip("Line between the visible client and the server's latest authoritative position.")]
    public Color lineColorNeutral = new Color(0.75f, 0.85f, 0.95f, 0.8f);

    private GameObject ghostAvatar;
    private LineRenderer divergenceLine;
    private Transform groundRing;
    private Material ghostMaterial;
    private Material lineMaterial;
    private Vector3 currentServerPosition = Vector3.zero;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        CreateGhostAvatar();
        CreateDivergenceLine();
    }

    private void Update()
    {
        // Toggle shortcut: G key
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.gKey.wasPressedThisFrame)
        {
            ToggleGhostVisibility();
        }

        UpdateGhostTransform();
        UpdateDivergenceVector();
    }

    public void ToggleGhostVisibility()
    {
        isGhostVisible = !isGhostVisible;
        if (ghostAvatar != null) ghostAvatar.SetActive(isGhostVisible);
        if (divergenceLine != null) divergenceLine.enabled = isGhostVisible;
    }

    private void CreateGhostAvatar()
    {
        if (ghostAvatar != null) return;

        Shader urpShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        ghostMaterial = new Material(urpShader) { name = "Server_Ghost_Mat" };
        ghostMaterial.color = new Color(0.2f, 0.85f, 1.0f, 0.55f); // Luminous cyan

        if (customGhostPrefab != null)
        {
            ghostAvatar = Instantiate(customGhostPrefab, Vector3.zero, Quaternion.identity);
            ghostAvatar.name = "Server_Authoritative_Ghost";
            ApplyGhostMaterialRecursive(ghostAvatar);
        }
        else
        {
            // Stylized holographic mannequin
            ghostAvatar = new GameObject("Server_Authoritative_Ghost");

            // Body pillar
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Ghost_Body";
            body.transform.SetParent(ghostAvatar.transform, false);
            body.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            Collider bCol = body.GetComponent<Collider>();
            if (bCol != null) Destroy(bCol);
            body.GetComponent<MeshRenderer>().sharedMaterial = ghostMaterial;

            // Head beacon
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Ghost_Head";
            head.transform.SetParent(ghostAvatar.transform, false);
            head.transform.localPosition = new Vector3(0f, 2.1f, 0f);
            head.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            Collider hCol = head.GetComponent<Collider>();
            if (hCol != null) Destroy(hCol);
            head.GetComponent<MeshRenderer>().sharedMaterial = ghostMaterial;

            // Directional pointer / visor
            GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Ghost_Visor";
            visor.transform.SetParent(ghostAvatar.transform, false);
            visor.transform.localPosition = new Vector3(0f, 2.1f, 0.28f);
            visor.transform.localScale = new Vector3(0.35f, 0.15f, 0.2f);
            Collider vCol = visor.GetComponent<Collider>();
            if (vCol != null) Destroy(vCol);
            visor.GetComponent<MeshRenderer>().sharedMaterial = ghostMaterial;
        }

        // Ground holographic beacon ring
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Server_Ground_Ring";
        ring.transform.SetParent(ghostAvatar.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        ring.transform.localScale = new Vector3(1.4f, 0.015f, 1.4f);
        Collider rCol = ring.GetComponent<Collider>();
        if (rCol != null) Destroy(rCol);
        
        Material ringMat = new Material(ghostMaterial) { name = "Ghost_Ring_Mat" };
        ringMat.color = new Color(0.1f, 0.7f, 1.0f, 0.7f);
        ring.GetComponent<MeshRenderer>().sharedMaterial = ringMat;
        groundRing = ring.transform;

        ghostAvatar.SetActive(isGhostVisible);
    }

    private void ApplyGhostMaterialRecursive(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            r.sharedMaterial = ghostMaterial;
        }
        Collider[] cols = obj.GetComponentsInChildren<Collider>();
        foreach (var c in cols)
        {
            Destroy(c);
        }
    }

    private void CreateDivergenceLine()
    {
        Shader urpShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        lineMaterial = new Material(urpShader) { name = "Divergence_Line_Mat" };

        GameObject lineObj = new GameObject("Divergence_Vector_Line");
        lineObj.transform.SetParent(transform, false);

        divergenceLine = lineObj.AddComponent<LineRenderer>();
        divergenceLine.material = lineMaterial;
        divergenceLine.startWidth = 0.08f;
        divergenceLine.endWidth = 0.08f;
        divergenceLine.positionCount = 2;
        divergenceLine.useWorldSpace = true;
        divergenceLine.enabled = isGhostVisible;
    }

    private void UpdateGhostTransform()
    {
        if (ServerAuthoritativeLoop.Instance != null)
        {
            currentServerPosition = ServerAuthoritativeLoop.Instance.AuthoritativePosition;
        }

        if (ghostAvatar != null)
        {
            ghostAvatar.transform.position = currentServerPosition;

            // Orient towards client movement facing direction if available
            if (ClientPrediction.LocalPlayer != null)
            {
                Vector3 toClient = ClientPrediction.LocalPlayer.transform.position - currentServerPosition;
                toClient.y = 0f;
                if (toClient.sqrMagnitude > 0.05f)
                {
                    ghostAvatar.transform.rotation = Quaternion.Slerp(
                        ghostAvatar.transform.rotation,
                        Quaternion.LookRotation(toClient),
                        10f * Time.deltaTime
                    );
                }
            }

            // Pulse ground ring
            if (groundRing != null)
            {
                float pulse = 1.3f + Mathf.Sin(Time.time * 4f) * 0.15f;
                groundRing.localScale = new Vector3(pulse, 0.015f, pulse);
            }
        }
    }

    private void UpdateDivergenceVector()
    {
        if (divergenceLine == null || !isGhostVisible) return;

        Vector3 clientPos = ClientPrediction.LocalPlayer != null
            ? ClientPrediction.LocalPlayer.transform.position
            : Vector3.zero;

        Vector3 serverPos = currentServerPosition;
        float distance = Vector3.Distance(clientPos, serverPos);

        if (distance < 0.03f)
        {
            divergenceLine.enabled = false;
            return;
        }

        divergenceLine.enabled = true;
        divergenceLine.SetPosition(0, clientPos + Vector3.up * 0.5f);
        divergenceLine.SetPosition(1, serverPos + Vector3.up * 0.5f);

        // Neutral colour on purpose: this gap is mostly normal latency (the server is
        // one trip behind the client), so it is not evidence of cheating by itself.
        Color lineColor = lineColorNeutral;
        divergenceLine.startColor = lineColor;
        divergenceLine.endColor = lineColor;
        if (lineMaterial != null) lineMaterial.color = lineColor;
    }
}
