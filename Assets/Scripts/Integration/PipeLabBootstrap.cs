using UnityEngine;

namespace PipeLab.Integration
{
    /// <summary>Creates the integration client and generated project view for any loaded scene.</summary>
    public static class PipeLabBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateClient()
        {
            if (Object.FindObjectOfType<PipeLabProjectClient>() != null) return;

            var root = new GameObject("PipeLab Integration");
            var client = root.AddComponent<PipeLabProjectClient>();
            var renderer = root.AddComponent<PipeLabProjectRenderer>();
            var hud = root.AddComponent<PipeLabProjectHud>();
            client.ConfigureFromEnvironment(
                System.Environment.GetEnvironmentVariable("PIPELAB_API_URL"),
                System.Environment.GetEnvironmentVariable("PIPELAB_PROJECT_ID"));
            renderer.SetClient(client);
            hud.SetReferences(client, renderer);
        }
    }
}
