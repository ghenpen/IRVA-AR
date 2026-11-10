using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARCore;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(ARFaceManager))]
public class ARFaceMaskController : MonoBehaviour
{
    [SerializeField] private GameObject[] maskPrefabs;
    [SerializeField] private Material blueMaskMaterial;
    [SerializeField] private Material occlusionMaterial;
    [SerializeField] private Button[] maskButtons;
    [SerializeField] private Button nextMaskButton;
    [SerializeField] private TMP_Text statusText;

    private static readonly string[] MaskNames = { "Albastra", "Ochelari", "Pisica", "Coroana" };
    private readonly Dictionary<TrackableId, GameObject> content = new Dictionary<TrackableId, GameObject>();
    private NativeArray<ARCoreFaceRegionData> regions;
    private ARFaceManager faceManager;
    private XROrigin origin;
    private int selectedMask;
    private bool initialized;

    private void Awake()
    {
        faceManager = GetComponent<ARFaceManager>();
        origin = GetComponent<XROrigin>();
        bool configured = origin != null && maskPrefabs != null && maskPrefabs.Length == MaskNames.Length - 1 &&
            blueMaskMaterial != null && occlusionMaterial != null && maskButtons != null &&
            maskButtons.Length == MaskNames.Length && nextMaskButton != null && statusText != null;
        if (configured)
        {
            for (int i = 0; i < maskPrefabs.Length; i++)
                configured &= maskPrefabs[i] != null;
            for (int i = 0; i < MaskNames.Length; i++)
                configured &= maskButtons[i] != null;
        }
        if (!configured)
        {
            Debug.LogError("ARFaceMaskController requires an XR Origin, three accessory prefabs, blue/depth materials, four buttons, Next and status text.", this);
            if (statusText != null)
                statusText.text = "Configuratia mastilor este incompleta.";
            enabled = false;
            RefreshButtons();
            return;
        }
        initialized = true;
    }

    private void OnEnable()
    {
        if (!initialized)
            return;
        faceManager.trackablesChanged.AddListener(OnFacesChanged);
        RefreshButtons();
        UpdateFaces();
    }

    private void LateUpdate()
    {
        if (initialized)
            UpdateFaces();
    }

    public void SelectMask(int index)
    {
        if (!initialized || !isActiveAndEnabled || index < 0 || index >= MaskNames.Length)
        {
            Debug.LogWarning("Selectarea mastii necesita un controller activ si un index intre 0 si 3.", this);
            if (statusText != null)
                statusText.text = "Masca nu poate fi selectata: verifica configuratia si indexul.";
            return;
        }
        if (selectedMask != index)
        {
            selectedMask = index;
            ClearContent();
        }
        RefreshButtons();
        UpdateFaces();
    }

    public void NextMask()
    {
        SelectMask((selectedMask + 1) % MaskNames.Length);
    }

    private void UpdateFaces()
    {
        ApplyFaceMaterial(selectedMask == 0 ? blueMaskMaterial : occlusionMaterial);
        HideContent();
        if (Application.isEditor)
        {
            SetStatus(0, "Accesoriile se testeaza pe tableta, cu camera frontala.");
            return;
        }
        if (ARSession.state == ARSessionState.Unsupported)
        {
            SetStatus(0, "Dispozitivul nu suporta aceasta sesiune AR.", true);
            return;
        }
        if (!faceManager.enabled)
        {
            SetStatus(0, "Managerul de detectare a fetelor este dezactivat.", true);
            return;
        }
        // ARCore's selfie camera does not track the world; faces can track during SessionInitializing.
        bool sessionRunning = ARSession.state == ARSessionState.SessionInitializing ||
            ARSession.state == ARSessionState.SessionTracking;
        if (!sessionRunning)
        {
            SetStatus(0, "Camera frontala se initializeaza (" + ARSession.state + ").");
            return;
        }
        if (faceManager.subsystem == null || !faceManager.subsystem.running)
        {
            SetStatus(0, "Asteapta pornirea detectarii fetelor.");
            return;
        }
        var subsystem = faceManager.subsystem as ARCoreFaceSubsystem;
        if (selectedMask != 0 && subsystem == null)
        {
            SetStatus(0, "Aceste accesorii necesita reperele faciale ARCore pe Android.", true);
            return;
        }

        int visible = 0;
        bool waitingForRegions = false;
        foreach (ARFace face in faceManager.trackables)
        {
            if (selectedMask == 0)
            {
                if (face.trackingState != TrackingState.None)
                    visible++;
                continue;
            }
            if (face.trackingState != TrackingState.Tracking)
                continue;
            subsystem.GetRegionPoses(face.trackableId, Allocator.Persistent, ref regions);
            if (!TryGetLayout(face, out Vector3 position, out float scale))
            {
                waitingForRegions = true;
                continue;
            }
            if (!content.TryGetValue(face.trackableId, out GameObject instance) || instance == null)
            {
                instance = Instantiate(maskPrefabs[selectedMask - 1], face.transform, false);
                content[face.trackableId] = instance;
            }
            instance.transform.SetLocalPositionAndRotation(position, Quaternion.identity);
            instance.transform.localScale = Vector3.one * scale;
            instance.SetActive(true);
            visible++;
        }
        SetStatus(visible, visible > 0 ? "Alege o masca sau apasa Urmatoarea." :
            waitingForRegions ? "Reperele fetei nu sunt disponibile. Priveste camera in lumina buna." :
            "Priveste camera frontala, cu fata bine luminata.");
    }

    private bool TryGetLayout(ARFace face, out Vector3 position, out float scale)
    {
        position = Vector3.zero;
        scale = 0;
        Vector3 nose = Vector3.zero, left = Vector3.zero, right = Vector3.zero;
        bool hasNose = false, hasLeft = false, hasRight = false;
        if (!regions.IsCreated)
            return false;
        foreach (ARCoreFaceRegionData region in regions)
        {
            // ARCore regions are session-space poses, not face-local or Unity world poses.
            Vector3 local = face.transform.InverseTransformPoint(
                origin.TrackablesParent.TransformPoint(region.pose.position));
            if (!IsFinite(local))
                return false;
            switch (region.region)
            {
                case ARCoreFaceRegion.NoseTip: nose = local; hasNose = true; break;
                case ARCoreFaceRegion.ForeheadLeft: left = local; hasLeft = true; break;
                case ARCoreFaceRegion.ForeheadRight: right = local; hasRight = true; break;
            }
        }
        if (!hasNose || !hasLeft || !hasRight)
            return false;
        scale = Vector3.Distance(left, right);
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0.001f)
            return false;
        Vector3 forehead = (left + right) * 0.5f;
        if (selectedMask == 1)
            position = Vector3.Lerp(nose, forehead, 0.45f) + Vector3.back * (scale * 0.12f);
        else if (selectedMask == 2)
            position = forehead + Vector3.up * (scale * 0.4f) + Vector3.forward * (scale * 0.1f);
        else
            position = forehead + Vector3.up * (scale * 0.35f) + Vector3.forward * (scale * 0.5f);
        return true;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private void ApplyFaceMaterial(Material material)
    {
        foreach (ARFace face in faceManager.trackables)
        {
            if (face == null)
                continue;
            var renderer = face.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != material)
                renderer.sharedMaterial = material;
        }
    }

    private void OnFacesChanged(ARTrackablesChangedEventArgs<ARFace> changes)
    {
        foreach (var removed in changes.removed)
        {
            if (content.TryGetValue(removed.Key, out GameObject instance))
            {
                if (instance != null)
                {
                    instance.SetActive(false);
                    Destroy(instance);
                }
                content.Remove(removed.Key);
            }
        }
    }

    private void SetStatus(int visible, string message, bool error = false)
    {
        string text = "Masca: " + MaskNames[selectedMask] + " (" + (selectedMask + 1) +
            "/" + MaskNames.Length + ")\nFete urmarite: " + visible + "\n" + message;
        if (statusText.text == text)
            return;
        statusText.text = text;
        if (error)
            Debug.LogWarning(message, this);
    }

    private void RefreshButtons()
    {
        bool active = initialized && isActiveAndEnabled;
        if (maskButtons != null)
            for (int i = 0; i < maskButtons.Length; i++)
                if (maskButtons[i] != null)
                    maskButtons[i].interactable = active && i != selectedMask;
        if (nextMaskButton != null)
            nextMaskButton.interactable = active;
    }

    private void HideContent()
    {
        foreach (GameObject instance in content.Values)
            if (instance != null)
                instance.SetActive(false);
    }

    private void ClearContent()
    {
        HideContent();
        foreach (GameObject instance in content.Values)
            if (instance != null)
                Destroy(instance);
        content.Clear();
    }

    private void OnDisable()
    {
        if (faceManager != null)
        {
            faceManager.trackablesChanged.RemoveListener(OnFacesChanged);
            if (blueMaskMaterial != null)
                ApplyFaceMaterial(blueMaskMaterial);
        }
        ClearContent();
        if (regions.IsCreated)
            regions.Dispose();
        regions = default;
        RefreshButtons();
    }
}
