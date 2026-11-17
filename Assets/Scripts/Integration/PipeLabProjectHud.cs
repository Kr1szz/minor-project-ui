using UnityEngine;

namespace PipeLab.Integration
{
    /// <summary>Lightweight runtime HUD for connection state, selection, and valve toggles.</summary>
    public sealed class PipeLabProjectHud : MonoBehaviour
    {
        [SerializeField] private PipeLabProjectClient projectClient;
        [SerializeField] private PipeLabProjectRenderer projectRenderer;
        [SerializeField] private bool showKeyboardHelp = true;

        private string selectedSummary = "Select a component";
        private string lastStatus = "Not connected";

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();
            if (projectClient != null) OnStatusChanged(projectClient.Status);
        }

        private void ResolveReferences()
        {
            if (projectClient == null) projectClient = GetComponent<PipeLabProjectClient>();
            if (projectRenderer == null) projectRenderer = GetComponent<PipeLabProjectRenderer>();
            if (projectRenderer != null)
            {
                projectRenderer.ComponentSelected -= OnComponentSelected;
                projectRenderer.ComponentSelected += OnComponentSelected;
            }
            if (projectClient != null)
            {
                projectClient.StatusChanged -= OnStatusChanged;
                projectClient.StatusChanged += OnStatusChanged;
                lastStatus = projectClient.Status;
            }
        }

        private void OnDestroy()
        {
            if (projectRenderer != null) projectRenderer.ComponentSelected -= OnComponentSelected;
            if (projectClient != null) projectClient.StatusChanged -= OnStatusChanged;
        }

        private void OnComponentSelected(PipeLabComponentDto component)
        {
            if (component == null) { selectedSummary = "Select a component"; return; }
            selectedSummary = component.name + "  |  " + component.type +
                              ((component.type == "valve" || component.type == "tap")
                                  ? "  |  " + (component.open ? "Open" : "Closed")
                                  : "");
        }

        private void OnStatusChanged(string status) { lastStatus = status; }

        private void OnGUI()
        {
            const int pad = 16;
            GUI.Box(new Rect(pad, pad, 480, showKeyboardHelp ? 120 : 82), "PipeLab Unity Client");
            GUI.Label(new Rect(pad + 12, pad + 28, 450, 24), lastStatus);
            GUI.Label(new Rect(pad + 12, pad + 52, 450, 24), selectedSummary);
            if (showKeyboardHelp)
                GUI.Label(new Rect(pad + 12, pad + 78, 450, 30), "Click a part to select  |  Project refreshes every 5 seconds");
        }
    }
}
