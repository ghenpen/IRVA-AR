using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[DisallowMultipleComponent]
public class CloudAnchorObjectPlacement : MonoBehaviour, IPointerDownHandler
{
    [SerializeField] private GameObject prefab;
    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private ARAnchorManager anchorManager;
    [SerializeField] private ARCloudAnchorManager cloudManager;

    private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private Transform anchorRoot;

    public GameObject spawnedObject { get; private set; }
    public bool HasPlacement => spawnedObject != null;

    private void Awake()
    {
        if (prefab == null || raycastManager == null || planeManager == null ||
            anchorManager == null || cloudManager == null)
        {
            Debug.LogError("CloudAnchorObjectPlacement requires its prefab and all AR manager references.", this);
            enabled = false;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || HasPlacement)
            return;

        if (!cloudManager.isActiveAndEnabled)
        {
            cloudManager.SetStatus("Controllerul Cloud Anchors nu este activ. Verifica configurarea.");
            return;
        }

        if (cloudManager.IsBusy)
        {
            cloudManager.ShowOperationStatus();
            return;
        }

        if (ARSession.state != ARSessionState.SessionTracking ||
            anchorManager.subsystem == null || !anchorManager.subsystem.running)
        {
            cloudManager.SetStatus("Scaneaza mediul pe dispozitiv pana cand urmarirea AR este activa.");
            return;
        }

        if (!raycastManager.Raycast(eventData.position, hits, TrackableType.PlaneWithinPolygon))
        {
            cloudManager.SetStatus("Atinge un plan detectat pentru a plasa cubul.");
            return;
        }

        ARPlane plane = planeManager.GetPlane(hits[0].trackableId);
        if (plane == null)
        {
            cloudManager.SetStatus("Planul nu mai este disponibil. Scaneaza suprafata din nou.");
            return;
        }

        ARAnchor anchor = anchorManager.AttachAnchor(plane, hits[0].pose);
        if (anchor == null)
        {
            Debug.LogWarning("Could not attach the local Cloud Anchors placement to the plane.", this);
            cloudManager.SetStatus("Ancora locala nu a putut fi creata. Incearca din nou.");
            return;
        }

        if (RecreatePlacement(anchor.transform))
            cloudManager.QueueAnchor(anchor);
        else
            Destroy(anchor.gameObject);
    }

    public bool RecreatePlacement(Transform resolvedAnchor)
    {
        if (resolvedAnchor == null || HasPlacement)
        {
            Debug.LogError("Cannot create placement without an anchor or while another cube exists.", this);
            cloudManager.SetStatus("Cubul nu poate fi creat: ancora lipseste sau exista deja un cub.");
            return false;
        }

        anchorRoot = resolvedAnchor;
        spawnedObject = Instantiate(prefab, anchorRoot, false);
        spawnedObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        return true;
    }

    public void RemovePlacement()
    {
        if (cloudManager.IsBusy)
        {
            cloudManager.ShowOperationStatus();
            return;
        }

        if (!HasPlacement)
        {
            cloudManager.SetStatus("Nu exista un cub de sters.");
            return;
        }

        ReleasePlacement();
        cloudManager.ClearLocalAnchor();
        cloudManager.SetStatus(cloudManager.HasSavedAnchor
            ? "Cub sters doar local. Apasa Resolve si scaneaza aceeasi zona."
            : "Cub sters. Plaseaza un cub nou si foloseste Host pentru a-l salva.");
    }

    private void OnDestroy()
    {
        ReleasePlacement();
    }

    private void ReleasePlacement()
    {
        if (anchorRoot != null)
            Destroy(anchorRoot.gameObject);
        else if (spawnedObject != null)
            Destroy(spawnedObject);

        anchorRoot = null;
        spawnedObject = null;
    }
}
