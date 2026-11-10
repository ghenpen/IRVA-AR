using System.Collections;
using Google.XR.ARCoreExtensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(ARAnchorManager))]
public class ARCloudAnchorManager : MonoBehaviour
{
    [SerializeField] private Camera arCamera;
    [SerializeField] private TMP_Text statusUpdate;
    [SerializeField] private CloudAnchorObjectPlacement placement;
    [SerializeField] private ARCoreExtensions extensions;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button resolveButton;
    [SerializeField] private Button deleteButton;

    private const string SavedAnchorKey = "IRVA.L3.CloudAnchorId";
    private const double ResolveTimeoutSeconds = 60;
    private const double MappingRefreshSeconds = 0.25;
    private ARAnchorManager arAnchorManager;
    private ARAnchor pendingHostAnchor;
    private string anchorIdToResolve;
    private HostCloudAnchorPromise hostPromise;
    private ResolveCloudAnchorPromise resolvePromise;
    private Coroutine operation;
    private bool initialized;
    private double resolveDeadline;
    private double nextMappingUpdate;
    private FeatureMapQuality? mappingQuality;
    private ARSessionState lastSessionState;
    private NotTrackingReason lastTrackingReason;
    private string statusMessage = string.Empty;
    private string trackingMessage = "Tracking: se initializeaza.";
    private string qualityMessage = "Calitate Host: neevaluata.";
    private string qualityUnavailableReason;

    public bool IsBusy => hostPromise != null || resolvePromise != null;
    public bool HasSavedAnchor => !string.IsNullOrEmpty(anchorIdToResolve);
    private bool HasAcceptableMappingQuality =>
        mappingQuality == FeatureMapQuality.Sufficient || mappingQuality == FeatureMapQuality.Good;

    private void Awake()
    {
        arAnchorManager = GetComponent<ARAnchorManager>();
        if (arCamera == null || statusUpdate == null || placement == null ||
            extensions == null || hostButton == null || resolveButton == null || deleteButton == null)
        {
            Debug.LogError("ARCloudAnchorManager requires its camera, status, placement, extensions and buttons.", this);
            enabled = false;
            return;
        }

        initialized = true;
        anchorIdToResolve = PlayerPrefs.GetString(SavedAnchorKey, string.Empty);
        SetStatus(HasSavedAnchor
            ? "Exista o ancora salvata. Apasa Resolve in aceeasi zona sau plaseaza un cub nou."
            : "Scaneaza un plan si atinge-l pentru a plasa un singur cub.");
        RefreshButtons();
    }

    private void Update()
    {
        if (!initialized)
            return;

        RefreshMappingStatus();
        RefreshButtons();
    }

    public void QueueAnchor(ARAnchor anchor)
    {
        if (anchor == null || IsBusy)
        {
            Debug.LogError("Cannot queue a missing local anchor or replace it during a cloud operation.", this);
            SetStatus("Ancora locala nu poate fi pregatita pentru Host.");
            return;
        }

        pendingHostAnchor = anchor;
        mappingQuality = null;
        nextMappingUpdate = 0;
        SetStatus("Cub plasat. Scaneaza zona din mai multe unghiuri, apoi apasa Host.");
        RefreshButtons();
    }

    public void ClearLocalAnchor()
    {
        pendingHostAnchor = null;
        RefreshButtons();
    }

    public void SetStatus(string text)
    {
        statusMessage = text;
        if (statusUpdate != null)
            RenderStatus();
        else
            Debug.LogWarning(text, this);
    }

    private void RenderStatus()
    {
        string text = trackingMessage + "\n" + qualityMessage + "\n\n" + statusMessage;
        if (statusUpdate != null && statusUpdate.text != text)
            statusUpdate.text = text;
    }

    private void RefreshMappingStatus(bool force = false)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        ARSessionState sessionState = ARSession.state;
        NotTrackingReason trackingReason = ARSession.notTrackingReason;
        if (!force && now < nextMappingUpdate &&
            sessionState == lastSessionState && trackingReason == lastTrackingReason)
            return;

        nextMappingUpdate = now + MappingRefreshSeconds;
        lastSessionState = sessionState;
        lastTrackingReason = trackingReason;
        trackingMessage = "Tracking: " + sessionState;
        if (sessionState != ARSessionState.SessionTracking && trackingReason != NotTrackingReason.None)
            trackingMessage += " (" + trackingReason + ")";

        mappingQuality = null;
        qualityUnavailableReason = GetQualityUnavailableReason();
        if (qualityUnavailableReason == null && IsBusy)
            qualityUnavailableReason = "operatie Cloud Anchors in curs";

        if (qualityUnavailableReason != null)
        {
            qualityMessage = "Calitate Host: indisponibila - " + qualityUnavailableReason + ".";
        }
        else
        {
            // ARCore expects session-space poses, without XR Origin or camera-height offsets.
            Pose cameraPose = extensions.Origin.TrackablesParent.InverseTransformPose(
                new Pose(arCamera.transform.position, arCamera.transform.rotation));
            mappingQuality = arAnchorManager.EstimateFeatureMapQualityForHosting(cameraPose);
            qualityMessage = "Calitate Host: " + mappingQuality.Value +
                (HasAcceptableMappingQuality
                    ? " (acceptata)."
                    : " (Host blocat; continua scanarea din mai multe pozitii).");
        }

        RenderStatus();
    }

    private string GetQualityUnavailableReason()
    {
        if (Application.isEditor)
            return "evaluarea se face pe dispozitiv";

        if (extensions == null || !extensions.isActiveAndEnabled ||
            extensions.ARCoreExtensionsConfig == null ||
            extensions.ARCoreExtensionsConfig.CloudAnchorMode != CloudAnchorMode.Enabled)
            return "ARCore Extensions sau Cloud Anchor Mode nu este activ";

        if (ARSession.state != ARSessionState.SessionTracking)
            return "tracking AR inactiv; scaneaza lent o zona luminata, cu detalii";

        if (extensions.Session == null || !extensions.Session.isActiveAndEnabled ||
            extensions.Session.subsystem == null || !extensions.Session.subsystem.running ||
            arAnchorManager == null || arAnchorManager.subsystem == null || !arAnchorManager.subsystem.running)
            return "sesiunea AR sau subsistemul de ancore nu este pornit";

        if (extensions.Origin == null || extensions.Origin.TrackablesParent == null ||
            arCamera == null || !arCamera.isActiveAndEnabled)
            return "XR Origin sau camera AR nu este pregatita";

        return null;
    }

    public void Host()
    {
        if (!CanUseCloud())
            return;

        if (pendingHostAnchor == null || !placement.HasPlacement)
        {
            SetStatus("Plaseaza mai intai un cub nou pentru Host.");
            return;
        }

        if (pendingHostAnchor.trackingState != TrackingState.Tracking)
        {
            SetStatus("Ancora locala nu este urmarita. Scaneaza din nou zona cubului.");
            return;
        }

        RefreshMappingStatus(true);
        if (!HasAcceptableMappingQuality)
        {
            SetStatus(mappingQuality.HasValue
                ? "Host blocat: calitatea trebuie sa fie Sufficient sau Good. Continua scanarea; nu se trimite nicio cerere."
                : "Host blocat: " + qualityUnavailableReason + ".");
            RefreshButtons();
            return;
        }

        Debug.Log("Cloud Anchor Host requested. Feature-map quality: " + mappingQuality.Value + ".", this);
        SetStatus("Host in curs. Calitatea hartii: " + mappingQuality.Value + ". Pastreaza zona in cadru.");
        // API-key authentication permits at most one day of hosting.
        hostPromise = arAnchorManager.HostCloudAnchorAsync(pendingHostAnchor, 1);
        RefreshButtons();
        operation = StartCoroutine(WaitHostingResult(hostPromise));
    }

    public void Resolve()
    {
        if (!CanUseCloud())
            return;

        if (!HasSavedAnchor)
        {
            SetStatus("Nu exista o ancora salvata. Plaseaza un cub si finalizeaza Host.");
            return;
        }

        if (placement.HasPlacement)
        {
            SetStatus("Apasa Delete pentru a elimina cubul local inainte de Resolve.");
            return;
        }

        resolveDeadline = Time.realtimeSinceStartupAsDouble + ResolveTimeoutSeconds;
        resolvePromise = arAnchorManager.ResolveCloudAnchorAsync(anchorIdToResolve);
        Debug.Log("Cloud Anchor Resolve requested; timeout is 60 seconds.", this);
        ShowOperationStatus();
        RefreshButtons();
        operation = StartCoroutine(WaitResolvingResult(resolvePromise));
    }

    public void ShowOperationStatus()
    {
        if (resolvePromise == null)
            return;

        int remaining = Mathf.Max(0, Mathf.CeilToInt(
            (float)(resolveDeadline - Time.realtimeSinceStartupAsDouble)));
        string guidance = ARSession.state == ARSessionState.SessionTracking
            ? "Scaneaza zona originala din mai multe unghiuri, cu detaliile de la Host."
            : "Tracking indisponibil (" + ARSession.notTrackingReason +
                "). Misca lent tableta spre o zona luminata, cu detalii.";
        SetStatus("Resolve in curs (" + remaining + " s ramase). " + guidance);
    }

    private bool CanUseCloud()
    {
        if (!initialized || !isActiveAndEnabled)
        {
            SetStatus("Controllerul Cloud Anchors nu este configurat sau activ.");
            return false;
        }

        if (IsBusy)
        {
            ShowOperationStatus();
            return false;
        }

        string unavailableReason = GetQualityUnavailableReason();
        if (unavailableReason != null)
        {
            RefreshMappingStatus(true);
            SetStatus("Operatie Cloud Anchors indisponibila: " + unavailableReason + ".");
            return false;
        }

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            SetStatus("Cloud Anchors necesita o conexiune la internet.");
            return false;
        }

        return true;
    }

    private IEnumerator WaitHostingResult(HostCloudAnchorPromise promise)
    {
        yield return promise;
        hostPromise = null;
        operation = null;

        if (promise.State == PromiseState.Cancelled)
        {
            SetStatus("Operatia Host a fost anulata.");
        }
        else
        {
            HostCloudAnchorResult result = promise.Result;
            if (result.CloudAnchorState == CloudAnchorState.Success)
            {
                if (string.IsNullOrEmpty(result.CloudAnchorId))
                {
                    Debug.LogError("Hosting completed without a Cloud Anchor ID.", this);
                    SetStatus("Host nu a returnat un ID valid. Reincearca.");
                }
                else
                {
                    SaveAnchorId(result.CloudAnchorId);
                    Debug.Log("Cloud Anchor hosting succeeded.", this);
                    SetStatus("Ancora salvata pentru o zi. Apasa Delete, apoi Resolve.");
                }
            }
            else
            {
                ReportFailure("Host", result.CloudAnchorState);
            }
        }

        RefreshButtons();
    }

    private IEnumerator WaitResolvingResult(ResolveCloudAnchorPromise promise)
    {
        // Defer even completed promises until StartCoroutine has assigned its handle.
        yield return null;
        bool timedOut = false;
        double nextStatusUpdate = 0;
        while (promise.State == PromiseState.Pending)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= resolveDeadline)
            {
                timedOut = true;
                promise.Cancel();
                break;
            }

            if (now >= nextStatusUpdate)
            {
                ShowOperationStatus();
                nextStatusUpdate = now + 0.5;
            }

            yield return null;
        }

        resolvePromise = null;
        resolveDeadline = 0;
        operation = null;

        // Completion can win the race with cancellation; consume that result normally.
        if (timedOut && promise.State != PromiseState.Done)
        {
            Debug.LogWarning("Cloud Anchor Resolve timed out after 60 seconds. Session: " +
                ARSession.state + "; tracking reason: " + ARSession.notTrackingReason + ".", this);
            SetStatus("Resolve oprit dupa 60 s. ID-ul salvat a fost pastrat. " +
                "Verifica trackingul si internetul, scaneaza zona originala, apoi reincearca Resolve.");
        }
        else if (promise.State == PromiseState.Cancelled)
        {
            SetStatus("Operatia Resolve a fost anulata.");
        }
        else
        {
            ResolveCloudAnchorResult result = promise.Result;
            if (result.CloudAnchorState == CloudAnchorState.Success)
            {
                if (result.Anchor == null)
                {
                    Debug.LogError("Resolve completed without a Cloud Anchor object.", this);
                    SetStatus("Resolve nu a returnat o ancora valida. Reincearca.");
                }
                else if (placement.RecreatePlacement(result.Anchor.transform))
                {
                    Debug.Log("Cloud Anchor resolving succeeded.", this);
                    SetStatus("Ancora regasita. Cubul este afisat la pozitia salvata in cloud.");
                }
                else
                    Destroy(result.Anchor.gameObject);
            }
            else
            {
                ReportFailure("Resolve", result.CloudAnchorState);
                if (result.Anchor != null)
                    Destroy(result.Anchor.gameObject);
            }
        }

        RefreshButtons();
    }

    private void SaveAnchorId(string id)
    {
        anchorIdToResolve = id;
        PlayerPrefs.SetString(SavedAnchorKey, id);
        PlayerPrefs.Save();
        pendingHostAnchor = null;
    }

    private void ReportFailure(string action, CloudAnchorState state)
    {
        string hint;
        switch (state)
        {
            case CloudAnchorState.ErrorNotAuthorized:
                hint = "Verifica autorizarea cheii si activarea ARCore API in Google Cloud.";
                break;
            case CloudAnchorState.ErrorResolvingCloudIdNotFound:
                hint = "Ancora poate fi expirata. Plaseaza un cub nou si foloseste Host.";
                break;
            case CloudAnchorState.ErrorHostingServiceUnavailable:
                hint = "Verifica internetul si reincearca.";
                break;
            case CloudAnchorState.ErrorHostingDatasetProcessingFailed:
                hint = "Serverul nu a putut procesa scanarea. Pastreaza trackingul stabil si scaneaza mai multe detalii inainte de un nou Host.";
                break;
            case CloudAnchorState.ErrorResourceExhausted:
                hint = "Verifica limita de cereri a proiectului Google Cloud.";
                break;
            default:
                hint = "Verifica eroarea si scaneaza din nou zona inainte de a reincerca.";
                break;
        }

        Debug.LogWarning(action + " failed: " + state, this);
        SetStatus(action + " a esuat: " + state + ". " + hint);
    }

    private void RefreshButtons()
    {
        if (!initialized)
            return;

        bool idle = isActiveAndEnabled && !IsBusy;
        hostButton.interactable = idle && placement.HasPlacement && pendingHostAnchor != null &&
            pendingHostAnchor.trackingState == TrackingState.Tracking &&
            HasAcceptableMappingQuality && GetQualityUnavailableReason() == null;
        resolveButton.interactable = idle && !placement.HasPlacement && HasSavedAnchor;
        deleteButton.interactable = idle && placement.HasPlacement;
    }

    private void OnDisable()
    {
        mappingQuality = null;
        nextMappingUpdate = 0;
        trackingMessage = "Tracking: controller inactiv.";
        qualityMessage = "Calitate Host: neevaluata.";
        if (operation != null)
            StopCoroutine(operation);
        operation = null;
        resolveDeadline = 0;

        if (hostPromise != null)
        {
            hostPromise.Cancel();
            if (hostPromise.State == PromiseState.Done &&
                hostPromise.Result.CloudAnchorState == CloudAnchorState.Success &&
                !string.IsNullOrEmpty(hostPromise.Result.CloudAnchorId))
            {
                SaveAnchorId(hostPromise.Result.CloudAnchorId);
            }
            hostPromise = null;
        }

        if (resolvePromise != null)
        {
            resolvePromise.Cancel();
            if (resolvePromise.State == PromiseState.Done && resolvePromise.Result.Anchor != null)
                Destroy(resolvePromise.Result.Anchor.gameObject);
            resolvePromise = null;
        }

        if (initialized)
        {
            if (statusUpdate != null)
                SetStatus("Controller oprit. Operatiile in asteptare au fost anulate local.");
            if (hostButton != null)
                hostButton.interactable = false;
            if (resolveButton != null)
                resolveButton.interactable = false;
            if (deleteButton != null)
                deleteButton.interactable = false;
        }
    }
}
