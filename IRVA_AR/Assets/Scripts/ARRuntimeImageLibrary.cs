using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(ARTrackedImageManager))]
public class ARRuntimeImageLibrary : MonoBehaviour
{
    public GameObject m_PlacedPrefab;
    [SerializeField] private XRReferenceImageLibrary initialLibrary;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button initialLibraryButton;
    [SerializeField] private Button runtimeLibraryButton;

    private const string RuntimeImageName = "Footprint";
    private const string RuntimeImageUrl =
        "https://images-assets.nasa.gov/image/KSC-as11-40-5877/KSC-as11-40-5877~medium.jpg";

    private readonly Dictionary<TrackableId, GameObject> content = new Dictionary<TrackableId, GameObject>();
    private readonly HashSet<Guid> activeImageIds = new HashSet<Guid>();
    private ARTrackedImageManager trackImageManager;
    private RuntimeReferenceImageLibrary runtimeLibrary;
    private UnityWebRequest downloadRequest;
    private Texture2D downloadedTexture;
    private AddReferenceImageJobState addImageJob;
    private Coroutine loading;
    private bool jobPending;
    private bool busy;
    private bool initialized;
    private bool usingRuntimeLibrary;
    private string message;

    private void Awake()
    {
        trackImageManager = GetComponent<ARTrackedImageManager>();
        if (initialLibrary == null || initialLibrary.count < 3 || m_PlacedPrefab == null ||
            statusText == null || initialLibraryButton == null || runtimeLibraryButton == null)
        {
            Debug.LogError("ARRuntimeImageLibrary requires at least three reference images, a prefab and its UI references.", this);
            enabled = false;
            return;
        }

        initialized = true;
        CacheImageIds(initialLibrary);
        SetMessage("Arata camerei Earth, Astronaut sau LunarRover. Imaginea trebuie vazuta complet, fara reflexii.");
        RefreshButtons();
    }

    private void OnEnable()
    {
        if (!initialized)
            return;

        trackImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
        RefreshTrackedContent();
        RefreshButtons();
    }

    public void UseInitialLibrary()
    {
        if (!CanChangeLibrary())
            return;

        if (TryActivateLibrary(initialLibrary, false))
            SetMessage("Biblioteca initiala activa. Scaneaza Earth, Astronaut sau LunarRover.");
    }

    public void UseRuntimeLibrary()
    {
        if (!CanChangeLibrary())
            return;

        if (runtimeLibrary != null)
        {
            if (TryActivateLibrary(runtimeLibrary, true))
                SetMessage("Biblioteca runtime activa. Scaneaza Footprint-Runtime.");
            return;
        }

        if (trackImageManager.descriptor == null || !trackImageManager.descriptor.supportsMutableLibrary)
        {
            ReportError("Dispozitivul nu permite biblioteci de imagini modificabile la runtime.");
            return;
        }

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            ReportError("Descarcarea imaginii runtime necesita internet. Biblioteca initiala ramane activa.");
            return;
        }

        busy = true;
        RefreshButtons();
        SetMessage("Se descarca imaginea Footprint de la NASA...");
        loading = StartCoroutine(CreateRuntimeLibrary());
    }

    private bool CanChangeLibrary()
    {
        if (!initialized || !isActiveAndEnabled)
        {
            ReportError("Controllerul pentru imagini nu este configurat sau activ.");
            return false;
        }

        if (busy)
        {
            SetMessage("Asteapta descarcarea si validarea imaginii runtime.");
            return false;
        }

        if (Application.isEditor)
        {
            SetMessage("Detectarea si biblioteca runtime se testeaza in build-ul de pe tableta.");
            return false;
        }

        if (ARSession.state != ARSessionState.SessionTracking || trackImageManager.subsystem == null)
        {
            SetMessage("Asteapta pornirea trackingului AR si scaneaza lent mediul.");
            return false;
        }

        return true;
    }

    private IEnumerator CreateRuntimeLibrary()
    {
        yield return null;
        try
        {
            downloadRequest = UnityWebRequestTexture.GetTexture(RuntimeImageUrl, false);
            downloadRequest.timeout = 30;
            yield return downloadRequest.SendWebRequest();
            if (downloadRequest.result != UnityWebRequest.Result.Success)
            {
                ReportError("Descarcarea a esuat: " + downloadRequest.error +
                    ". Biblioteca existenta este pastrata; poti reincerca.");
                yield break;
            }

            downloadedTexture = DownloadHandlerTexture.GetContent(downloadRequest);
            if (downloadedTexture == null || downloadedTexture.width < 300 || downloadedTexture.height < 300)
            {
                ReportError("Imaginea descarcata nu este valida sau are sub 300 x 300 pixeli.");
                yield break;
            }

            if (ARSession.state != ARSessionState.SessionTracking)
            {
                ReportError("Trackingul s-a pierdut in timpul descarcarii. Scaneaza din nou si reincearca.");
                yield break;
            }

            SetMessage("Se creeaza biblioteca runtime si se valideaza imaginea...");
            if (!TryScheduleImage(downloadedTexture, out MutableRuntimeReferenceImageLibrary candidate))
                yield break;

            // The AR Foundation texture overload copies the pixels before scheduling the job.
            Destroy(downloadedTexture);
            downloadedTexture = null;
            while (!addImageJob.jobHandle.IsCompleted)
                yield return null;
            addImageJob.jobHandle.Complete();
            jobPending = false;

            if (addImageJob.status != AddReferenceImageJobStatus.Success || candidate.count != 1)
            {
                ReportError("Imaginea nu a fost adaugata: " + addImageJob.status +
                    ". Biblioteca existenta este pastrata.");
                yield break;
            }

            runtimeLibrary = candidate;
            if (TryActivateLibrary(runtimeLibrary, true))
                SetMessage("Biblioteca runtime creata cu 1 imagine. Scaneaza Footprint-Runtime.");
        }
        finally
        {
            ReleaseLoadingResources();
            busy = false;
            loading = null;
            RefreshButtons();
        }
    }

    private bool TryScheduleImage(Texture2D texture, out MutableRuntimeReferenceImageLibrary candidate)
    {
        candidate = null;
        try
        {
            candidate = trackImageManager.CreateRuntimeLibrary() as MutableRuntimeReferenceImageLibrary;
            if (candidate == null)
            {
                ReportError("Providerul AR nu a creat o biblioteca modificabila.");
                return false;
            }

            addImageJob = candidate.ScheduleAddImageWithValidationJob(texture, RuntimeImageName, null);
            jobPending = true;
            return true;
        }
        catch (Exception exception) when (exception is NotSupportedException ||
            exception is InvalidOperationException || exception is ArgumentException)
        {
            ReportError("Imaginea runtime nu poate fi pregatita: " + exception.Message);
            return false;
        }
    }

    private bool TryActivateLibrary(IReferenceImageLibrary library, bool runtime)
    {
        IReferenceImageLibrary previousLibrary = trackImageManager.referenceLibrary;
        bool wasEnabled = trackImageManager.enabled;
        try
        {
            trackImageManager.referenceLibrary = library;
            trackImageManager.requestedMaxNumberOfMovingImages = 3;
            trackImageManager.enabled = true;
        }
        catch (Exception exception) when (exception is NotSupportedException ||
            exception is InvalidOperationException || exception is ArgumentException)
        {
            trackImageManager.referenceLibrary = previousLibrary;
            trackImageManager.enabled = wasEnabled;
            ReportError("Biblioteca nu a putut fi activata: " + exception.Message);
            return false;
        }

        ClearContent();
        CacheImageIds(library);
        usingRuntimeLibrary = runtime;
        RefreshTrackedContent();
        RefreshButtons();
        return true;
    }

    private void RefreshTrackedContent()
    {
        // A cached target need not emit a new event when its library is selected again.
        foreach (ARTrackedImage image in trackImageManager.trackables)
            UpdateContent(image);
        RefreshStatus();
    }

    private void CacheImageIds(IReferenceImageLibrary library)
    {
        activeImageIds.Clear();
        for (int i = 0; i < library.count; i++)
            activeImageIds.Add(library[i].guid);
    }

    private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> changes)
    {
        foreach (ARTrackedImage image in changes.added)
            UpdateContent(image);
        foreach (ARTrackedImage image in changes.updated)
            UpdateContent(image);
        foreach (var removed in changes.removed)
        {
            if (content.TryGetValue(removed.Key, out GameObject instance))
            {
                if (instance != null)
                    Destroy(instance);
                content.Remove(removed.Key);
            }
        }
        RefreshStatus();
    }

    private void UpdateContent(ARTrackedImage image)
    {
        bool visible = activeImageIds.Contains(image.referenceImage.guid) &&
            image.trackingState == TrackingState.Tracking;
        if (content.TryGetValue(image.trackableId, out GameObject instance) && instance != null)
        {
            instance.SetActive(visible);
        }
        else if (visible)
        {
            instance = Instantiate(m_PlacedPrefab, image.transform, false);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            content[image.trackableId] = instance;
        }
    }

    private void SetMessage(string text)
    {
        message = text;
        RefreshStatus();
    }

    private void ReportError(string text)
    {
        Debug.LogWarning(text, this);
        SetMessage(text);
    }

    private void RefreshStatus()
    {
        if (statusText == null)
            return;

        int visible = 0;
        foreach (GameObject instance in content.Values)
            if (instance != null && instance.activeSelf)
                visible++;
        string text = (usingRuntimeLibrary ? "Runtime: Footprint (1 imagine)" : "Biblioteca initiala: 3 imagini") +
            "\nImagini urmarite: " + visible + "\n" + message;
        if (statusText.text != text)
            statusText.text = text;
    }

    private void RefreshButtons()
    {
        if (!initialized)
            return;
        bool idle = isActiveAndEnabled && !busy;
        if (initialLibraryButton != null)
            initialLibraryButton.interactable = idle && usingRuntimeLibrary;
        if (runtimeLibraryButton != null)
            runtimeLibraryButton.interactable = idle && !usingRuntimeLibrary;
    }

    private void ClearContent()
    {
        foreach (GameObject instance in content.Values)
            if (instance != null)
            {
                instance.SetActive(false);
                Destroy(instance);
            }
        content.Clear();
    }

    private void ReleaseLoadingResources()
    {
        if (downloadRequest != null)
        {
            downloadRequest.Abort();
            downloadRequest.Dispose();
            downloadRequest = null;
        }
        if (jobPending)
        {
            addImageJob.jobHandle.Complete();
            jobPending = false;
        }
        if (downloadedTexture != null)
        {
            Destroy(downloadedTexture);
            downloadedTexture = null;
        }
    }

    private void OnDisable()
    {
        if (trackImageManager != null)
            trackImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
        if (loading != null)
            StopCoroutine(loading);
        ReleaseLoadingResources();
        loading = null;
        busy = false;
        ClearContent();
        RefreshButtons();
    }
}
