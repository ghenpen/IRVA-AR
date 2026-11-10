using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class ARStatisticsDisplay : MonoBehaviour
{
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private ARPointCloudManager pointCloudManager;
    [SerializeField] private Transform arCamera;
    [SerializeField] private TMP_Text statisticsText;

    private const float RefreshInterval = 0.1f;
    private float nextRefreshTime;

    private void Awake()
    {
        if (planeManager == null || pointCloudManager == null ||
            arCamera == null || statisticsText == null)
        {
            Debug.LogError(
                "ARStatisticsDisplay requires both AR managers, the AR camera and a text component.",
                this);
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + RefreshInterval;
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        int pointCount = 0;
        foreach (var pointCloud in pointCloudManager.trackables)
        {
            var positions = pointCloud.positions;
            if (positions.HasValue)
                pointCount += positions.Value.Length;
        }

        Vector3 position = arCamera.position;
        Vector3 rotation = arCamera.eulerAngles;

        statisticsText.SetText(
            "Plane: {0}  |  Puncte: {1}\n" +
            "Pozitie camera (m, world):\n" +
            "X: {2:0.00}  Y: {3:0.00}  Z: {4:0.00}\n" +
            "Orientare camera (grade, Euler):\n" +
            "X: {5:0.0}  Y: {6:0.0}  Z: {7:0.0}",
            planeManager.trackables.count, pointCount,
            position.x, position.y, position.z,
            rotation.x, rotation.y, rotation.z);
    }
}
