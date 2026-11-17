using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace PipeLab.Integration
{
    /// <summary>Read-only polling client for PipeLab project snapshots.</summary>
    public sealed class PipeLabProjectClient : MonoBehaviour
    {
        [Header("PipeLab API")]
        [SerializeField] private string apiBaseUrl = "http://127.0.0.1:8000";
        [SerializeField] private string projectId = "";
        [SerializeField, Min(1f)] private float requestTimeoutSeconds = 10f;
        [SerializeField, Min(1f)] private float refreshIntervalSeconds = 5f;
        [SerializeField] private bool connectOnStart = true;

        public PipeLabProjectSnapshot CurrentProject { get; private set; }
        public string Status { get; private set; } = "Not connected";
        public event Action<PipeLabProjectSnapshot> SnapshotLoaded;
        public event Action<string> StatusChanged;

        private bool snapshotRequestInProgress;
        private bool polling;

        private void Start()
        {
            if (connectOnStart) Connect();
        }

        public void Connect()
        {
            if (polling) return;
            string unusedUrl;
            string error;
            if (!TryBuildSnapshotUrl(out unusedUrl, out error))
            {
                SetStatus(error);
                return;
            }
            StartCoroutine(PollRoutine());
        }

        public void FetchSnapshot()
        {
            if (!snapshotRequestInProgress) StartCoroutine(FetchSnapshotRoutine());
        }

        public PipeLabComponentDto FindComponent(string id)
        {
            if (CurrentProject?.components == null) return null;
            foreach (PipeLabComponentDto component in CurrentProject.components)
                if (component != null && component.id == id) return component;
            return null;
        }

        private IEnumerator PollRoutine()
        {
            polling = true;
            while (enabled && gameObject.activeInHierarchy)
            {
                yield return FetchSnapshotRoutine();
                yield return new WaitForSeconds(Mathf.Max(1f, refreshIntervalSeconds));
            }
            polling = false;
        }

        private IEnumerator FetchSnapshotRoutine()
        {
            if (snapshotRequestInProgress) yield break;
            string url;
            string error;
            if (!TryBuildSnapshotUrl(out url, out error))
            {
                SetStatus(error);
                yield break;
            }

            snapshotRequestInProgress = true;
            SetStatus("Loading project snapshot…");
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = Mathf.Max(1, Mathf.CeilToInt(requestTimeoutSeconds));
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    SetStatus("Snapshot failed: " + request.error);
                    snapshotRequestInProgress = false;
                    yield break;
                }

                PipeLabProjectSnapshot snapshot = null;
                string parseError = null;
                try
                {
                    snapshot = JsonUtility.FromJson<PipeLabProjectSnapshot>(request.downloadHandler.text);
                    parseError = ValidateSnapshot(snapshot);
                }
                catch (Exception exception) { parseError = exception.Message; }

                if (parseError != null)
                {
                    SetStatus("Invalid snapshot: " + parseError);
                    snapshotRequestInProgress = false;
                    yield break;
                }
                if (!string.IsNullOrEmpty(projectId) && snapshot.id != projectId)
                {
                    SetStatus("Snapshot project ID does not match the configured ID.");
                    snapshotRequestInProgress = false;
                    yield break;
                }

                CurrentProject = snapshot;
                SnapshotLoaded?.Invoke(snapshot);
                SetStatus("Loaded " + snapshot.name + " (" + snapshot.components.Length + " components)");
            }
            snapshotRequestInProgress = false;
        }

        private bool TryBuildSnapshotUrl(out string url, out string error)
        {
            url = null;
            error = null;
            if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out Uri baseUri) ||
                (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            {
                error = "Enter a valid HTTP or HTTPS API base URL.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(projectId))
            {
                error = "Enter a PipeLab project ID in the Inspector.";
                return false;
            }
            url = baseUri.ToString().TrimEnd('/') + "/projects/" + UnityWebRequest.EscapeURL(projectId.Trim());
            return true;
        }

        private static string ValidateSnapshot(PipeLabProjectSnapshot snapshot)
        {
            if (snapshot == null) return "response was empty or not a JSON object";
            if (string.IsNullOrWhiteSpace(snapshot.id)) return "project ID is missing";
            if (snapshot.components == null) return "components list is missing";
            if (snapshot.connections == null) return "connections list is missing";
            if (!IsFinite(snapshot.source_pressure)) return "source pressure is not finite";

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (PipeLabComponentDto component in snapshot.components)
            {
                if (component == null || string.IsNullOrWhiteSpace(component.id)) return "a component has no ID";
                if (!ids.Add(component.id)) return "duplicate component ID: " + component.id;
                if (string.IsNullOrWhiteSpace(component.type)) return "component " + component.id + " has no type";
                if (!HasThreeFiniteValues(component.position)) return "component " + component.id + " has an invalid position";
                if (!IsFinite(component.rotation) || !IsFinite(component.diameter) || !IsFinite(component.length))
                    return "component " + component.id + " has non-finite numeric values";
            }
            foreach (PipeLabConnectionDto connection in snapshot.connections)
            {
                if (connection == null || string.IsNullOrWhiteSpace(connection.id)) return "a connection has no ID";
                if (!ids.Contains(connection.from_component) || !ids.Contains(connection.to_component))
                    return "connection " + connection.id + " refers to a missing component";
            }
            return null;
        }

        private static bool HasThreeFiniteValues(float[] values)
        {
            return values != null && values.Length >= 3 &&
                   IsFinite(values[0]) && IsFinite(values[1]) && IsFinite(values[2]);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void SetStatus(string status)
        {
            Status = status;
            Debug.Log("[PipeLab] " + status, this);
            StatusChanged?.Invoke(status);
        }
    }
}
