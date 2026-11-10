using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARRaycastManager), typeof(ARPlaneManager), typeof(ARAnchorManager))]
public class MeasureDistances : MonoBehaviour
{
    public Camera FirstPersonCamera;
    public GameObject pointPrefab;
    public GameObject linePrefab;
    public TMP_Text textPrefab;
    public Canvas parent;
    public TMP_Text totalDistance;
    public TMP_Text statusText;

    public GameObject spawnedObject { get; private set; }
    public readonly List<GameObject> points = new List<GameObject>();
    public readonly List<TMP_Text> distances = new List<TMP_Text>();

    private const float LineWidth = 0.005f;
    private const string PlacementHint = "Atinge un plan pentru un punct nou sau un cub pentru a-l muta.";
    private readonly List<GameObject> lines = new List<GameObject>();
    private readonly List<ARAnchor> anchors = new List<ARAnchor>();
    private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private ARRaycastManager raycastManager;
    private ARPlaneManager planeManager;
    private ARAnchorManager anchorManager;
    private int selectedPointIndex = -1;
    private Renderer selectedRenderer;
    private MaterialPropertyBlock originalSelectionProperties;

    private void Awake()
    {
        raycastManager = GetComponent<ARRaycastManager>();
        planeManager = GetComponent<ARPlaneManager>();
        anchorManager = GetComponent<ARAnchorManager>();

        if (FirstPersonCamera == null || pointPrefab == null || linePrefab == null ||
            textPrefab == null || parent == null || totalDistance == null || statusText == null)
        {
            Debug.LogError("MeasureDistances requires the AR camera, prefabs, Canvas and both HUD texts.", this);
            enabled = false;
            return;
        }

        if (pointPrefab.GetComponentInChildren<Collider>() == null ||
            pointPrefab.GetComponentInChildren<Renderer>() == null)
        {
            Debug.LogError("The measurement point prefab requires a collider and a renderer for selection.", this);
            statusText.text = "Prefab-ul punctului necesita un collider si un renderer.";
            enabled = false;
            return;
        }

        totalDistance.SetText("Distanta totala: {0:0.00} m", 0f);
        statusText.text = "Misca telefonul pentru a detecta plane, apoi atinge suprafata.";
    }

    private bool TryGetTouchPosition(out Vector2 position)
    {
        if (Touchscreen.current != null &&
            Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            position = Touchscreen.current.primaryTouch.position.ReadValue();
            return true;
        }

#if UNITY_EDITOR
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            position = Mouse.current.position.ReadValue();
            return true;
        }
#endif

        position = default;
        return false;
    }

    private bool IsPointerOverUI(Vector2 position)
    {
        if (EventSystem.current == null)
            return false;

        uiHits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current)
        {
            position = position
        }, uiHits);
        return uiHits.Count > 0;
    }

    private void Update()
    {
        if (!TryGetTouchPosition(out Vector2 position) || IsPointerOverUI(position))
            return;

        if (selectedPointIndex >= 0 && points[selectedPointIndex] == null)
        {
            UpdateMeasurements();
            return;
        }

        int touchedPointIndex = FindPointAt(position);
        if (touchedPointIndex >= 0)
        {
            if (touchedPointIndex == selectedPointIndex)
            {
                ClearSelection();
                statusText.text = PlacementHint;
            }
            else
            {
                SelectPoint(touchedPointIndex);
            }
            return;
        }

        if (ARSession.state != ARSessionState.SessionTracking ||
            anchorManager.subsystem == null || !anchorManager.subsystem.running)
        {
            statusText.text = "Urmarirea AR nu este disponibila. Scaneaza mediul pe un dispozitiv compatibil.";
            return;
        }

        if (!raycastManager.Raycast(position, hits, TrackableType.PlaneWithinPolygon))
        {
            statusText.text = selectedPointIndex >= 0
                ? "Atinge un plan detectat pentru mutare sau acelasi cub pentru anulare."
                : "Atinge o suprafata plana detectata.";
            return;
        }

        ARPlane plane = planeManager.GetPlane(hits[0].trackableId);
        if (plane == null)
        {
            statusText.text = "Planul nu mai este disponibil. Scaneaza suprafata din nou.";
            return;
        }

        ARAnchor anchor = anchorManager.AttachAnchor(plane, hits[0].pose);
        if (anchor == null)
        {
            Debug.LogWarning("MeasureDistances could not attach an AR anchor to the detected plane.", this);
            statusText.text = "Punctul nu a putut fi ancorat. Incearca din nou.";
            return;
        }

        if (selectedPointIndex >= 0)
            MoveSelectedPoint(anchor);
        else
            AddPoint(anchor);
    }

    private int FindPointAt(Vector2 screenPosition)
    {
        Ray ray = FirstPersonCamera.ScreenPointToRay(screenPosition);
        float nearestDistance = FirstPersonCamera.farClipPlane;
        int nearestIndex = -1;
        Physics.SyncTransforms();

        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] == null)
                continue;

            foreach (Collider collider in points[i].GetComponentsInChildren<Collider>())
            {
                if (collider.enabled && collider.Raycast(ray, out RaycastHit hit, nearestDistance))
                {
                    nearestDistance = hit.distance;
                    nearestIndex = i;
                }
            }
        }

        return nearestIndex;
    }

    private void SelectPoint(int index)
    {
        ClearSelection();
        selectedPointIndex = index;
        selectedRenderer = points[index].GetComponentInChildren<Renderer>();
        originalSelectionProperties = new MaterialPropertyBlock();
        selectedRenderer.GetPropertyBlock(originalSelectionProperties);

        var highlight = new MaterialPropertyBlock();
        selectedRenderer.GetPropertyBlock(highlight);
        Color selectionColor = new Color(1f, 0.8f, 0f, 1f);
        highlight.SetColor("_BaseColor", selectionColor);
        highlight.SetColor("_Color", selectionColor);
        selectedRenderer.SetPropertyBlock(highlight);

        statusText.SetText(
            "Punctul {0} selectat. Atinge un plan pentru mutare sau acelasi cub pentru anulare.",
            index + 1);
    }

    private void ClearSelection()
    {
        if (selectedRenderer != null)
            selectedRenderer.SetPropertyBlock(originalSelectionProperties);

        selectedRenderer = null;
        originalSelectionProperties = null;
        selectedPointIndex = -1;
    }

    private void MoveSelectedPoint(ARAnchor anchor)
    {
        int index = selectedPointIndex;
        ARAnchor previousAnchor = anchors[index];
        Transform point = points[index].transform;

        // Reparent before removing the old anchor, keeping the marker and its outgoing line alive.
        point.SetParent(anchor.transform, false);
        point.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        anchors[index] = anchor;
        ClearSelection();

        if (previousAnchor != null && previousAnchor != anchor)
            Destroy(previousAnchor.gameObject);

        statusText.text = "Punct mutat. " + PlacementHint;
        UpdateMeasurements();
    }

    private void AddPoint(ARAnchor anchor)
    {
        anchors.Add(anchor);
        spawnedObject = Instantiate(pointPrefab, anchor.transform, false);
        spawnedObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        points.Add(spawnedObject);

        if (points.Count > 1)
        {
            // Lines follow anchored endpoints instead of creating redundant, independently drifting anchors.
            lines.Add(Instantiate(linePrefab, points[points.Count - 2].transform, false));
            TMP_Text label = Instantiate(textPrefab, parent.transform, false);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.raycastTarget = false;
            distances.Add(label);
        }

        statusText.text = PlacementHint;
        UpdateMeasurements();
    }

    private void LateUpdate()
    {
        UpdateMeasurements();
    }

    private void UpdateMeasurements()
    {
        foreach (GameObject point in points)
        {
            if (point == null)
            {
                Debug.LogWarning("An AR measurement anchor was removed; measurements have been cleared.", this);
                ReleaseMeasurements();
                statusText.text = "O ancora a fost pierduta. Reia masuratoarea.";
                break;
            }
        }

        float sum = 0f;
        for (int i = 0; i < lines.Count; i++)
        {
            Vector3 start = points[i].transform.position;
            Vector3 end = points[i + 1].transform.position;
            Vector3 midpoint = (start + end) * 0.5f;
            float distance = Vector3.Distance(start, end);
            sum += distance;

            Transform line = lines[i].transform;
            line.position = midpoint;
            line.localScale = new Vector3(LineWidth, LineWidth, distance);
            line.gameObject.SetActive(distance > Mathf.Epsilon);
            if (distance > Mathf.Epsilon)
                line.LookAt(end);

            distances[i].SetText("{0:0.00} m", distance);
            UpdateLabelPosition(distances[i], midpoint);
        }

        totalDistance.SetText("Distanta totala: {0:0.00} m", sum);
    }

    private void UpdateLabelPosition(TMP_Text label, Vector3 worldPosition)
    {
        Vector3 screenPosition = FirstPersonCamera.WorldToScreenPoint(worldPosition);
        bool visible = screenPosition.z > FirstPersonCamera.nearClipPlane &&
            screenPosition.z < FirstPersonCamera.farClipPlane &&
            screenPosition.x >= 0f && screenPosition.x <= Screen.width &&
            screenPosition.y >= 0f && screenPosition.y <= Screen.height;
        label.gameObject.SetActive(visible);
        if (!visible)
            return;

        Camera uiCamera = parent.renderMode == RenderMode.ScreenSpaceOverlay ? null : parent.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)parent.transform, screenPosition, uiCamera, out Vector2 localPosition))
        {
            label.rectTransform.anchoredPosition = localPosition + new Vector2(0f, 24f);
        }
    }

    private void OnDisable()
    {
        if (selectedPointIndex >= 0 && statusText != null)
            statusText.text = PlacementHint;

        ClearSelection();
    }

    private void OnDestroy()
    {
        ReleaseMeasurements();
    }

    private void ReleaseMeasurements()
    {
        ClearSelection();

        foreach (TMP_Text label in distances)
            if (label != null)
                Destroy(label.gameObject);

        foreach (ARAnchor anchor in anchors)
            if (anchor != null)
                Destroy(anchor.gameObject);

        points.Clear();
        distances.Clear();
        lines.Clear();
        anchors.Clear();
        spawnedObject = null;
    }
}