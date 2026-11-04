using System.Collections.Generic;
using UnityEngine;

namespace PipeLab.Integration
{
    /// <summary>Minimal generated-primitive view of the authoritative PipeLab project graph.</summary>
    public sealed class PipeLabProjectRenderer : MonoBehaviour
    {
        [SerializeField] private PipeLabProjectClient projectClient;
        [SerializeField] private Transform contentRoot;
        [SerializeField] private float worldScale = 1f;
        [SerializeField] private float componentRadius = 0.12f;
        [SerializeField] private float connectionRadius = 0.025f;
        [SerializeField] private bool centerProject = true;

        private readonly Dictionary<string, GameObject> componentViews = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> connectionViews = new Dictionary<string, GameObject>();
        private Material defaultMaterial;
        private Material selectedMaterial;
        private string selectedComponentId;
        private Vector3 currentCenter;

        public string SelectedComponentId => selectedComponentId;
        public event System.Action<PipeLabComponentDto> ComponentSelected;

        public void SetClient(PipeLabProjectClient client)
        {
            if (projectClient == client) return;
            if (projectClient != null)
            {
                projectClient.SnapshotLoaded -= RenderSnapshot;
            }
            projectClient = client;
            if (isActiveAndEnabled && projectClient != null)
            {
                projectClient.SnapshotLoaded += RenderSnapshot;
                if (projectClient.CurrentProject != null) RenderSnapshot(projectClient.CurrentProject);
            }
            if (projectClient == null) ClearViews();
        }

        private void Awake()
        {
            if (contentRoot == null) contentRoot = transform;
            defaultMaterial = CreateMaterial(new Color(0.20f, 0.62f, 0.78f));
            selectedMaterial = CreateMaterial(new Color(1.0f, 0.72f, 0.16f));
        }

        private void OnEnable()
        {
            if (projectClient == null) projectClient = FindObjectOfType<PipeLabProjectClient>();
            if (projectClient == null) return;
            projectClient.SnapshotLoaded -= RenderSnapshot;
            projectClient.SnapshotLoaded += RenderSnapshot;
            if (projectClient.CurrentProject != null) RenderSnapshot(projectClient.CurrentProject);
        }

        private void OnDisable()
        {
            if (projectClient == null) return;
            projectClient.SnapshotLoaded -= RenderSnapshot;
            ClearViews();
        }

        private void OnDestroy()
        {
            if (defaultMaterial != null) Destroy(defaultMaterial);
            if (selectedMaterial != null) Destroy(selectedMaterial);
        }

        private void ClearViews()
        {
            foreach (GameObject view in componentViews.Values) if (view != null) Destroy(view);
            foreach (GameObject view in connectionViews.Values) if (view != null) Destroy(view);
            componentViews.Clear();
            connectionViews.Clear();
        }

        public void Select(string componentId)
        {
            selectedComponentId = componentId;
            RefreshComponentMaterials();
            ComponentSelected?.Invoke(projectClient != null ? projectClient.FindComponent(componentId) : null);
        }

        private void RenderSnapshot(PipeLabProjectSnapshot snapshot)
        {
            if (snapshot == null) return;

            var activeComponentIds = new HashSet<string>();
            var activeConnectionIds = new HashSet<string>();
            foreach (PipeLabComponentDto component in snapshot.components) activeComponentIds.Add(component.id);
            foreach (PipeLabConnectionDto connection in snapshot.connections) activeConnectionIds.Add(connection.id);
            RemoveStaleViews(componentViews, activeComponentIds);
            RemoveStaleViews(connectionViews, activeConnectionIds);

            Vector3 center = centerProject ? FindCenter(snapshot.components) : Vector3.zero;
            currentCenter = center;
            foreach (PipeLabConnectionDto connection in snapshot.connections)
                if (connectionViews.TryGetValue(connection.id, out GameObject oldView) && oldView != null)
                    Destroy(oldView);
            connectionViews.Clear();
            foreach (PipeLabComponentDto component in snapshot.components)
                RenderComponent(component, center);
            foreach (PipeLabConnectionDto connection in snapshot.connections)
                RenderConnection(connection);
            RefreshComponentMaterials();
        }

        private void RenderComponent(PipeLabComponentDto component, Vector3 center)
        {
            if (component == null || component.position == null || component.position.Length < 3) return;
            if (!componentViews.TryGetValue(component.id, out GameObject view) || view == null)
            {
                view = GameObject.CreatePrimitive(PrimitiveForType(component.type));
                view.name = "PipeLab " + component.type + " " + component.name + " [" + component.id + "]";
                view.transform.SetParent(contentRoot, false);
                view.AddComponent<PipeLabSelectable>().Initialize(this, component.id);
                componentViews[component.id] = view;
            }

            Vector3 raw = new Vector3(component.position[0], component.position[1], component.position[2]);
            view.transform.localPosition = (raw - center) * worldScale;
            view.transform.localRotation = Quaternion.Euler(0f, component.rotation, 0f);
            float size = Mathf.Max(0.05f, componentRadius * 2f);
            if (component.type == "pipe" || component.type == "curved-pipe")
                view.transform.localScale = new Vector3(size, size, Mathf.Max(size, component.length * worldScale));
            else
                view.transform.localScale = Vector3.one * size * (component.type == "tank" ? 1.6f : 1f);

            Renderer renderer = view.GetComponent<Renderer>();
            bool closed = (component.type == "valve" || component.type == "tap") && !component.open;
            ApplyComponentMaterial(renderer, component.id, closed);
        }

        private void RenderConnection(PipeLabConnectionDto connection)
        {
            if (connection == null || projectClient?.CurrentProject == null) return;
            PipeLabComponentDto from = projectClient.FindComponent(connection.from_component);
            PipeLabComponentDto to = projectClient.FindComponent(connection.to_component);
            if (from == null || to == null || from.position == null || to.position == null) return;
            Vector3 center = currentCenter;
            Vector3 a = Position(from.position, center);
            Vector3 b = Position(to.position, center);
            Vector3 delta = b - a;
            if (delta.sqrMagnitude < 0.0001f) return;

            if (!connectionViews.TryGetValue(connection.id, out GameObject view) || view == null)
            {
                view = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                view.name = "PipeLab connection " + connection.id;
                view.transform.SetParent(contentRoot, false);
                Collider collider = view.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                connectionViews[connection.id] = view;
            }
            view.transform.localPosition = (a + b) * 0.5f;
            view.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            view.transform.localScale = new Vector3(connectionRadius * 2f, delta.magnitude * 0.5f, connectionRadius * 2f);
            view.GetComponent<Renderer>().sharedMaterial = defaultMaterial;
        }

        private void RemoveStaleViews(Dictionary<string, GameObject> views, HashSet<string> activeIds)
        {
            var removed = new List<string>();
            foreach (var pair in views)
                if (!activeIds.Contains(pair.Key))
                {
                    if (pair.Value != null) Destroy(pair.Value);
                    removed.Add(pair.Key);
                }
            foreach (string id in removed) views.Remove(id);
            if (ReferenceEquals(views, componentViews) && selectedComponentId != null && !activeIds.Contains(selectedComponentId))
                selectedComponentId = null;
        }

        private void RefreshComponentMaterials()
        {
            if (projectClient?.CurrentProject == null) return;
            currentCenter = centerProject ? FindCenter(projectClient.CurrentProject.components) : Vector3.zero;
            foreach (PipeLabComponentDto component in projectClient.CurrentProject.components)
                if (component != null) RenderComponent(component, currentCenter);
            foreach (PipeLabConnectionDto connection in projectClient.CurrentProject.connections)
                RenderConnection(connection);
            foreach (var pair in componentViews)
            {
                if (pair.Value == null) continue;
                Renderer renderer = pair.Value.GetComponent<Renderer>();
                bool closed = false;
                PipeLabComponentDto component = projectClient != null ? projectClient.FindComponent(pair.Key) : null;
                if (component != null) closed = (component.type == "valve" || component.type == "tap") && !component.open;
                ApplyComponentMaterial(renderer, pair.Key, closed);
            }
        }

        private void ApplyComponentMaterial(Renderer renderer, string id, bool closed)
        {
            renderer.sharedMaterial = id == selectedComponentId ? selectedMaterial : defaultMaterial;
            Color tint = closed ? new Color(0.82f, 0.22f, 0.16f) :
                id == selectedComponentId ? new Color(1f, 0.72f, 0.16f) : Color.white;
            if (renderer.sharedMaterial.HasProperty("_BaseColor")) renderer.sharedMaterial.SetColor("_BaseColor", tint);
            else if (renderer.sharedMaterial.HasProperty("_Color")) renderer.sharedMaterial.SetColor("_Color", tint);
        }

        private Vector3 FindCenter(PipeLabComponentDto[] components)
        {
            if (components == null || components.Length == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (PipeLabComponentDto component in components)
            {
                if (component?.position == null || component.position.Length < 3) continue;
                sum += new Vector3(component.position[0], component.position[1], component.position[2]);
                count++;
            }
            return count == 0 ? Vector3.zero : sum / count;
        }

        private Vector3 Position(float[] position, Vector3 center)
        {
            return (new Vector3(position[0], position[1], position[2]) - center) * worldScale;
        }

        private static PrimitiveType PrimitiveForType(string type)
        {
            switch (type)
            {
                case "tank": return PrimitiveType.Cube;
                case "pipe":
                case "curved-pipe": return PrimitiveType.Cylinder;
                case "valve": return PrimitiveType.Cylinder;
                case "tap": return PrimitiveType.Capsule;
                default: return PrimitiveType.Sphere;
            }
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            return material;
        }
    }

    public sealed class PipeLabSelectable : MonoBehaviour
    {
        private PipeLabProjectRenderer owner;
        private string componentId;

        public void Initialize(PipeLabProjectRenderer renderer, string id)
        {
            owner = renderer;
            componentId = id;
        }

        private void OnMouseDown()
        {
            owner?.Select(componentId);
        }
    }
}
