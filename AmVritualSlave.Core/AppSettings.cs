using System.ComponentModel.DataAnnotations;

namespace AmVritualSlave.Core
{
    /// <summary>
    /// 应用配置映射类（精简版，去除 HMI 专用配置）
    /// </summary>
    public class AppSettings
    {
        public ModbusSettings Modbus { get; set; } = new();
        public SimulationSettings Simulation { get; set; } = new();
        public SerilogSettings Serilog { get; set; } = new();
        public OpcUaSettings OpcUa { get; set; } = new();
        public MqttSettings Mqtt { get; set; } = new();
    }

    public class ModbusSettings
    {
        public bool Enabled { get; set; } = true;
        /// <summary>Tcp 或 Rtu</summary>
        public string Mode { get; set; } = "Tcp";
        [Range(1, 247)]
        public byte SlaveId { get; set; } = 1;

        // TCP 模式参数
        [Range(1024, 65535)]
        public int Port { get; set; } = 5020;
        public string IpAddress { get; set; } = "0.0.0.0";

        // RTU 模式参数
        public SerialPortSettings SerialPort { get; set; } = new();
    }

    public class SerialPortSettings
    {
        public string PortName { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        /// <summary>None, Odd, Even, Mark, Space</summary>
        public string Parity { get; set; } = "None";
        public int DataBits { get; set; } = 8;
        /// <summary>None, One, Two, OnePointFive</summary>
        public string StopBits { get; set; } = "One";
    }

    public class SimulationSettings
    {
        public string InitialMode { get; set; } = "Random";
        public int TimeoutMs { get; set; } = 1000;
        public int UpdateIntervalMs { get; set; } = 2000;
        public float DefaultNoise { get; set; } = 1.0f;
        public int DefaultDelayMs { get; set; } = 0;
    }

    public class SerilogSettings
    {
        public string MinimumLevel { get; set; } = "Information";
    }

    public class OpcUaSettings
    {
        public bool Enabled { get; set; } = false;
        public int Port { get; set; } = 4840;
        public string ApplicationName { get; set; } = "AmVritualSlave";
        public string ApplicationUri { get; set; } = "urn:localhost:AmVritualSlave";
    }

    public class MqttSettings
    {
        public bool Enabled { get; set; } = false;
        public string Broker { get; set; } = "localhost";
        public int Port { get; set; } = 1883;
        public string TopicPrefix { get; set; } = "industrial";
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}
