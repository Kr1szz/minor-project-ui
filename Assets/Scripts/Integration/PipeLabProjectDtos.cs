using System;

namespace PipeLab.Integration
{
    [Serializable]
    public sealed class PipeLabProjectSnapshot
    {
        public string id;
        public string name;
        public float source_pressure;
        public PipeLabComponentDto[] components;
        public PipeLabConnectionDto[] connections;
    }

    [Serializable]
    public sealed class PipeLabComponentDto
    {
        public string id;
        public string type;
        public string name;
        public float[] position;
        public float rotation;
        public string material;
        public float diameter;
        public float length;
        public float max_pressure;
        public float pressure_loss;
        public float flow_resistance;
        public bool open;
        public float opening;
        public PipeLabPortDto[] ports;
        public string properties;
        public float pressure;
        public float flow;
        public string status;
    }

    [Serializable]
    public sealed class PipeLabPortDto
    {
        public string id;
        public string direction;
        public float[] position;
        public float[] vector;
        public float diameter;
    }

    [Serializable]
    public sealed class PipeLabConnectionDto
    {
        public string id;
        public string from_component;
        public string from_port;
        public string to_component;
        public string to_port;
    }

}
