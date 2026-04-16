using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace AmVritualSlave.Core
{
    /// <summary>
    /// OPC UA Server 服务：将 SharedData 暴露为 OPC UA 节点，支持 Subscription 订阅推送。
    /// </summary>
    public class OpcUaServerService : BackgroundService
    {
        private readonly SharedData _sharedData;
        private readonly IOptionsMonitor<AppSettings> _optionsMonitor;
        private OpcUaSettings _currentOpcConfig;
        private readonly ILogger _log;
        private IndustrialServer? _server;
        private CancellationTokenSource? _restartCts;

        public OpcUaServerService(
            SharedData sharedData,
            IOptionsMonitor<AppSettings> optionsMonitor)
        {
            _sharedData = sharedData;
            _optionsMonitor = optionsMonitor;
            _currentOpcConfig = optionsMonitor.CurrentValue.OpcUa;
            _log = Log.ForContext("SourceContext", "OpcUaServer");

            _optionsMonitor.OnChange(appSettings =>
            {
                var newConfig = appSettings.OpcUa;

                if (newConfig.Enabled != _currentOpcConfig.Enabled)
                {
                    _log.Information("[HotReload] OpcUa Enabled changed: {Old} -> {New}, triggering restart...",
                        _currentOpcConfig.Enabled, newConfig.Enabled);
                    _restartCts?.Cancel();
                }
                else if (newConfig.Enabled && _currentOpcConfig.Enabled &&
                    (newConfig.Port != _currentOpcConfig.Port ||
                     newConfig.ApplicationName != _currentOpcConfig.ApplicationName ||
                     newConfig.ApplicationUri != _currentOpcConfig.ApplicationUri))
                {
                    _log.Information("[HotReload] Critical config changed (Port/AppName/AppUri), triggering server restart...");
                    _restartCts?.Cancel();
                }

                _currentOpcConfig = newConfig;
                _log.Information("[HotReload] OpcUa config updated: Enabled={Enabled}, Port={Port}, AppName={AppName}",
                    newConfig.Enabled, newConfig.Port, newConfig.ApplicationName);
            });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_currentOpcConfig.Enabled)
                {
                    _log.Information("[OpcUa] Server is disabled in configuration, waiting for enable...");
                    _restartCts = new CancellationTokenSource();
                    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(
                        stoppingToken, _restartCts.Token);
                    try
                    {
                        await Task.Delay(-1, waitCts.Token);
                    }
                    catch (OperationCanceledException) { }

                    if (stoppingToken.IsCancellationRequested) break;
                    continue;
                }

                _restartCts = new CancellationTokenSource();
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken, _restartCts.Token);

                try
                {
                    await RunServerAsync(linkedCts.Token);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "[OpcUa] Server error");
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    _log.Information("[OpcUa] Server shutdown requested.");
                    break;
                }

                _log.Information("[OpcUa] Server restarting with new configuration...");
                try
                {
                    await Task.Delay(1000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task RunServerAsync(CancellationToken cancellationToken)
        {
            var settings = _currentOpcConfig;
            var config = CreateConfiguration();

            EnsureCertificateDirectories();
            EnsureApplicationCertificate(config);

            _server = new IndustrialServer(_sharedData, settings);
            await _server.StartAsync(config);

            _log.Information("[OpcUa] Server started on port {Port}", settings.Port);
            _log.Information("[OpcUa] Endpoint: opc.tcp://localhost:{Port}", settings.Port);

            _sharedData.DataChanged += OnDataChanged;

            try
            {
                var tcs = new TaskCompletionSource();
                using var registration = cancellationToken.Register(() =>
                {
                    _sharedData.DataChanged -= OnDataChanged;
                    _server.StopAsync().AsTask().Wait();
                    _log.Information("[OpcUa] Server stopped");
                    tcs.TrySetResult();
                });

                await tcs.Task;
            }
            finally
            {
                _sharedData.DataChanged -= OnDataChanged;
                _server?.Dispose();
                _server = null;
            }
        }

        private void OnDataChanged(object? sender, DataChangedEventArgs e)
        {
            try
            {
                _server?.NotifyDataChanged();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[OpcUa] Error notifying data change");
            }
        }

        private ApplicationConfiguration CreateConfiguration()
        {
            var applicationUri = _currentOpcConfig.ApplicationUri;
            var applicationName = _currentOpcConfig.ApplicationName;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var certPath = Path.Combine(baseDir, "OPC UA", "Certificates");
            var trustedPath = Path.Combine(baseDir, "OPC UA", "Certificates", "Trusted");
            var issuerPath = Path.Combine(baseDir, "OPC UA", "Issuers");

            var config = new ApplicationConfiguration
            {
                ApplicationName = applicationName,
                ApplicationUri = applicationUri,
                ApplicationType = ApplicationType.Server,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = certPath,
                        SubjectName = $"CN={applicationName}, O=AmVritualSlave, C=CN"
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = trustedPath
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = issuerPath
                    },
                    AutoAcceptUntrustedCertificates = true,
                    AddAppCertToTrustedStore = true
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = 15000,
                    MaxStringLength = 1048576,
                    MaxByteStringLength = 1048576,
                    MaxArrayLength = 65535,
                    MaxMessageSize = 4194304,
                    MaxBufferSize = 65535,
                    ChannelLifetime = 300000,
                    SecurityTokenLifetime = 3600000
                },
                ServerConfiguration = new ServerConfiguration
                {
                    ServerCapabilities = new StringCollection { "DA" },
                    MinRequestThreadCount = 2,
                    MaxRequestThreadCount = 10,
                    MaxQueuedRequestCount = 200,
                    BaseAddresses = new StringCollection
                    {
                        $"opc.tcp://localhost:{_currentOpcConfig.Port}"
                    },
                    SecurityPolicies = new ServerSecurityPolicyCollection
                    {
                        new ServerSecurityPolicy
                        {
                            SecurityMode = MessageSecurityMode.None,
                            SecurityPolicyUri = SecurityPolicies.None
                        }
                    },
                    UserTokenPolicies = new UserTokenPolicyCollection
                    {
                        new UserTokenPolicy(UserTokenType.Anonymous)
                    }
                }
            };

            return config;
        }

        private void EnsureCertificateDirectories()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var certPath = Path.Combine(baseDir, "OPC UA", "Certificates");
                var trustedPath = Path.Combine(baseDir, "OPC UA", "Certificates", "Trusted");
                var issuerPath = Path.Combine(baseDir, "OPC UA", "Issuers");

                if (!Directory.Exists(certPath)) Directory.CreateDirectory(certPath);
                if (!Directory.Exists(trustedPath)) Directory.CreateDirectory(trustedPath);
                if (!Directory.Exists(issuerPath)) Directory.CreateDirectory(issuerPath);

                _log.Information("[OpcUa] Certificate directories created at {BaseDir}", baseDir);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[OpcUa] Failed to create certificate directories");
            }
        }

        private void EnsureApplicationCertificate(ApplicationConfiguration config)
        {
            try
            {
                var certPath = config.SecurityConfiguration.ApplicationCertificate.StorePath;
                var subjectName = config.SecurityConfiguration.ApplicationCertificate.SubjectName;
                var applicationUri = config.ApplicationUri;

                if (!Directory.Exists(certPath))
                {
                    Directory.CreateDirectory(certPath);
                }

                var existingCerts = Directory.GetFiles(certPath, "*.pfx");
                if (existingCerts.Length > 0)
                {
                    _log.Information("[OpcUa] Using existing certificate from: {Path}", existingCerts[0]);

                    //var cert = X509CertificateLoader.LoadCertificateFromFile(existingCerts[0]);
                    var cert = new X509Certificate2(existingCerts[0]);

                    config.SecurityConfiguration.ApplicationCertificate.Certificate = cert;

                    return;
                }

                _log.Information("[OpcUa] Generating self-signed certificate...");

                ushort keySize = 2048;
                var notBefore = DateTime.UtcNow.AddDays(-1);
                var notAfter = DateTime.UtcNow.AddMonths(12);

                var domainNames = new List<string> { "localhost", Environment.MachineName };

                var certBuilder = CertificateFactory.CreateCertificate(
                    applicationUri,
                    subjectName,
                    null,
                    domainNames
                );

                certBuilder.SetNotBefore(notBefore);
                certBuilder.SetNotAfter(notAfter);
                certBuilder.SetRSAKeySize(keySize);

                var newCert = certBuilder.CreateForRSA();

                var certFileName = Path.Combine(certPath, $"{Guid.NewGuid()}.pfx");
                File.WriteAllBytes(certFileName, newCert.Export(X509ContentType.Pkcs12));

                config.SecurityConfiguration.ApplicationCertificate.Certificate = newCert;

                _log.Information("[OpcUa] Certificate generated: {Subject}", newCert.Subject);
                _log.Information("[OpcUa] Certificate saved to: {Path}", certFileName);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[OpcUa] Certificate generation warning, continuing...");
            }
        }

        public override void Dispose()
        {
            _server?.Dispose();
            base.Dispose();
        }
    }

    /// <summary>
    /// 工业数据 OPC UA 服务器
    /// </summary>
    internal class IndustrialServer : StandardServer
    {
        private readonly SharedData _sharedData;
        private readonly OpcUaSettings _settings;
        private IndustrialNodeManager? _nodeManager;

        public IndustrialServer(SharedData sharedData, OpcUaSettings settings)
        {
            _sharedData = sharedData;
            _settings = settings;
        }

        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            _nodeManager = new IndustrialNodeManager(server, configuration, _sharedData);
            return new MasterNodeManager(server, configuration, null, new INodeManager[] { _nodeManager });
        }

        protected override ServerProperties LoadServerProperties()
        {
            return new ServerProperties
            {
                ManufacturerName = "AmVritualSlave",
                ProductName = "Virtual Slave OPC UA Server",
                ProductUri = "http://amvirtualslave.org",
                SoftwareVersion = "2.0.0",
                BuildNumber = "2.0.0",
                BuildDate = DateTime.UtcNow
            };
        }

        public void NotifyDataChanged()
        {
            _nodeManager?.NotifyDataChanged();
        }
    }

    /// <summary>
    /// 节点管理器：定义 OPC UA 地址空间，按分组创建子文件夹
    /// </summary>
    internal class IndustrialNodeManager : CustomNodeManager2
    {
        private readonly SharedData _sharedData;
        private readonly List<BaseDataVariableState> _variables = new();
        private readonly ILogger _log = Log.ForContext("SourceContext", "OpcUaServer");

        public IndustrialNodeManager(IServerInternal server, ApplicationConfiguration config, SharedData sharedData)
            : base(server, config, new[] { "http://amvirtualslave.org/Industrial" })
        {
            _sharedData = sharedData;
            SystemContext.NodeIdFactory = this;
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                base.CreateAddressSpace(externalReferences);

                IList<IReference>? references = null;
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                var rootFolder = CreateRootFolder(ref references);

                // === Sensors 文件夹 (只读传感器值) ===
                var sensorsFolder = CreateFolder(rootFolder, "Sensors", "Sensor Readings");
                CreateVariable(sensorsFolder, "Temperature", "Temperature (°C)", DataTypeIds.Double,
                    () => (double)_sharedData.SensorSnapshot().Temperature, writable: false,
                    engineeringUnit: "°C", rangeLow: -40, rangeHigh: 150);
                CreateVariable(sensorsFolder, "Pressure", "Pressure (kPa)", DataTypeIds.Double,
                    () => (double)_sharedData.SensorSnapshot().Pressure, writable: false,
                    engineeringUnit: "kPa", rangeLow: 0, rangeHigh: 1000);
                CreateVariable(sensorsFolder, "FlowRate", "Flow Rate (m³/h)", DataTypeIds.Double,
                    () => (double)_sharedData.SensorSnapshot().FlowRate, writable: false,
                    engineeringUnit: "m³/h", rangeLow: 0, rangeHigh: 500);
                CreateVariable(sensorsFolder, "Level", "Level (%)", DataTypeIds.Double,
                    () => (double)_sharedData.SensorSnapshot().Level, writable: false,
                    engineeringUnit: "%", rangeLow: 0, rangeHigh: 100);
                CreateVariable(sensorsFolder, "Humidity", "Humidity (%RH)", DataTypeIds.Double,
                    () => (double)_sharedData.SensorSnapshot().Humidity, writable: false,
                    engineeringUnit: "%RH", rangeLow: 0, rangeHigh: 100);
                CreateVariable(sensorsFolder, "Rpm", "Motor Speed (RPM)", DataTypeIds.Int32,
                    () => _sharedData.SensorSnapshot().Rpm, writable: false,
                    engineeringUnit: "RPM", rangeLow: 0, rangeHigh: 3600);
                CreateVariable(sensorsFolder, "Voltage", "Voltage (V)", DataTypeIds.Double,
                    () => (double)_sharedData.SensorSnapshot().Voltage, writable: false,
                    engineeringUnit: "V", rangeLow: 0, rangeHigh: 500);
                CreateVariable(sensorsFolder, "Current", "Current (A)", DataTypeIds.Double,
                    () => (double)_sharedData.SensorSnapshot().Current, writable: false,
                    engineeringUnit: "A", rangeLow: 0, rangeHigh: 100);
                CreateVariable(sensorsFolder, "Power", "Active Power (W)", DataTypeIds.Double,
                    () => (double)_sharedData.SensorSnapshot().Power, writable: false,
                    engineeringUnit: "W", rangeLow: 0, rangeHigh: 50000);
                CreateVariable(sensorsFolder, "Frequency", "Frequency (Hz)", DataTypeIds.Double,
                    () => (double)_sharedData.SensorSnapshot().Frequency, writable: false,
                    engineeringUnit: "Hz", rangeLow: 45, rangeHigh: 55);

                // === Parameters 文件夹 (可写控制参数) ===
                var paramsFolder = CreateFolder(rootFolder, "Parameters", "Control Parameters");
                CreateVariable(paramsFolder, "Running", "Device Running", DataTypeIds.Boolean,
                    () => _sharedData.GetRunning(), writable: true,
                    writeAction: val => _sharedData.SetCoilRun((bool)val));
                CreateVariable(paramsFolder, "Mode", "Simulation Mode", DataTypeIds.Int32,
                    () => (int)_sharedData.GetMode(), writable: true,
                    writeAction: val =>
                    {
                        var m = (SharedData.SimulationMode)(int)val;
                        if (Enum.IsDefined(typeof(SharedData.SimulationMode), m))
                            _sharedData.SetMode(m);
                    });
                CreateVariable(paramsFolder, "NoiseMultiplier", "Noise Multiplier", DataTypeIds.Float,
                    () => _sharedData.GetNoiseMultiplier(), writable: true,
                    writeAction: val => _sharedData.SetNoiseMultiplier((float)val));
                CreateVariable(paramsFolder, "ResponseDelayMs", "Response Delay (ms)", DataTypeIds.Int32,
                    () => _sharedData.GetResponseDelayMs(), writable: true,
                    writeAction: val => _sharedData.SetResponseDelay((int)val));
                CreateVariable(paramsFolder, "SamplePeriod", "Sample Period (ms)", DataTypeIds.Int32,
                    () => _sharedData.GetSamplePeriod(), writable: true,
                    writeAction: val => _sharedData.SetSamplePeriod((int)val));
                CreateVariable(paramsFolder, "AlarmHighLimit", "Alarm High Limit (°C)", DataTypeIds.Float,
                    () => _sharedData.GetAlarmHighLimit(), writable: true,
                    writeAction: val => _sharedData.SetAlarmHighLimit((float)val));
                CreateVariable(paramsFolder, "AlarmLowLimit", "Alarm Low Limit (°C)", DataTypeIds.Float,
                    () => _sharedData.GetAlarmLowLimit(), writable: true,
                    writeAction: val => _sharedData.SetAlarmLowLimit((float)val));
                CreateVariable(paramsFolder, "PowerFactor", "Power Factor (cosφ)", DataTypeIds.Float,
                    () => _sharedData.GetPowerFactor(), writable: true,
                    writeAction: val => _sharedData.SetPowerFactor((float)val));

                // === Statistics 文件夹 (只读统计) ===
                var statsFolder = CreateFolder(rootFolder, "Statistics", "Statistical Data");
                CreateVariable(statsFolder, "TempMax", "Temperature Max (°C)", DataTypeIds.Double,
                    () => (double)_sharedData.GetTempMaxReg() / 10.0, writable: false,
                    engineeringUnit: "°C", rangeLow: -40, rangeHigh: 150);
                CreateVariable(statsFolder, "TempMin", "Temperature Min (°C)", DataTypeIds.Double,
                    () => (double)_sharedData.GetTempMinReg() / 10.0, writable: false,
                    engineeringUnit: "°C", rangeLow: -40, rangeHigh: 150);
                CreateVariable(statsFolder, "TempAvg", "Temperature Avg (°C)", DataTypeIds.Double,
                    () => (double)_sharedData.GetTempAvgReg() / 10.0, writable: false,
                    engineeringUnit: "°C", rangeLow: -40, rangeHigh: 150);
                CreateVariable(statsFolder, "PressMax", "Pressure Max (kPa)", DataTypeIds.Double,
                    () => (double)_sharedData.GetPressMaxReg() / 10.0, writable: false,
                    engineeringUnit: "kPa", rangeLow: 0, rangeHigh: 1000);
                CreateVariable(statsFolder, "PressMin", "Pressure Min (kPa)", DataTypeIds.Double,
                    () => (double)_sharedData.GetPressMinReg() / 10.0, writable: false,
                    engineeringUnit: "kPa", rangeLow: 0, rangeHigh: 1000);
                CreateVariable(statsFolder, "PressAvg", "Pressure Avg (kPa)", DataTypeIds.Double,
                    () => (double)_sharedData.GetPressAvgReg() / 10.0, writable: false,
                    engineeringUnit: "kPa", rangeLow: 0, rangeHigh: 1000);
                CreateVariable(statsFolder, "RunHours", "Run Hours", DataTypeIds.Int32,
                    () => (int)_sharedData.GetRunHoursReg(), writable: false);
                CreateVariable(statsFolder, "StartCount", "Start Count", DataTypeIds.Int32,
                    () => (int)_sharedData.GetStartCountReg(), writable: false);
                CreateVariable(statsFolder, "CommCount", "Communication Count", DataTypeIds.Int32,
                    () => (int)_sharedData.GetCommCountReg(), writable: false);
                CreateVariable(statsFolder, "ErrorCount", "Error Count", DataTypeIds.Int32,
                    () => (int)_sharedData.GetErrorCountReg(), writable: false);

                // === Alarms 文件夹 (报警/故障) ===
                var alarmsFolder = CreateFolder(rootFolder, "Alarms", "Alarm & Fault Data");
                CreateVariable(alarmsFolder, "AlarmEnableMask", "Alarm Enable Mask", DataTypeIds.UInt16,
                    () => _sharedData.GetAlarmEnableMask(), writable: true,
                    writeAction: val => _sharedData.SetAlarmEnableMask((ushort)val));
                CreateVariable(alarmsFolder, "AlarmStatusMask", "Alarm Status Mask", DataTypeIds.UInt16,
                    () => _sharedData.GetAlarmStatusMask(), writable: false);
                CreateVariable(alarmsFolder, "FaultCode1", "Fault Code 1", DataTypeIds.UInt16,
                    () => _sharedData.GetFaultCode1(), writable: true,
                    writeAction: val => _sharedData.SetFaultCode1((ushort)val));
                CreateVariable(alarmsFolder, "FaultCode2", "Fault Code 2", DataTypeIds.UInt16,
                    () => _sharedData.GetFaultCode2(), writable: true,
                    writeAction: val => _sharedData.SetFaultCode2((ushort)val));
                CreateVariable(alarmsFolder, "FaultCode3", "Fault Code 3", DataTypeIds.UInt16,
                    () => _sharedData.GetFaultCode3(), writable: true,
                    writeAction: val => _sharedData.SetFaultCode3((ushort)val));
                CreateVariable(alarmsFolder, "FaultCode4", "Fault Code 4", DataTypeIds.UInt16,
                    () => _sharedData.GetFaultCode4(), writable: true,
                    writeAction: val => _sharedData.SetFaultCode4((ushort)val));
                CreateVariable(alarmsFolder, "DiRunning", "DI Running", DataTypeIds.Boolean,
                    () => _sharedData.GetDiRunning(), writable: false);
                CreateVariable(alarmsFolder, "DiAlarm", "DI Alarm", DataTypeIds.Boolean,
                    () => _sharedData.GetDiAlarm(), writable: false);
                CreateVariable(alarmsFolder, "DiTempHigh", "DI Temp High", DataTypeIds.Boolean,
                    () => _sharedData.GetDiTempHigh(), writable: false);
                CreateVariable(alarmsFolder, "DiTempLow", "DI Temp Low", DataTypeIds.Boolean,
                    () => _sharedData.GetDiTempLow(), writable: false);
                CreateVariable(alarmsFolder, "DiCommOk", "DI Comm OK", DataTypeIds.Boolean,
                    () => _sharedData.GetDiCommOk(), writable: false);
                CreateVariable(alarmsFolder, "DiReady", "DI Ready", DataTypeIds.Boolean,
                    () => _sharedData.GetDiReady(), writable: false);

                // === Methods 文件夹 (设备操作方法) ===
                var methodsFolder = CreateFolder(rootFolder, "Methods", "Device Operations");
                CreateMethod(methodsFolder, "Start", "Start the device",
                    Array.Empty<Argument>(), Array.Empty<Argument>(),
                    args => { _sharedData.SetCoilRun(true); _log.Information("[OpcUa] Method: Start()"); return Array.Empty<object>(); });
                CreateMethod(methodsFolder, "Stop", "Stop the device",
                    Array.Empty<Argument>(), Array.Empty<Argument>(),
                    args => { _sharedData.SetCoilRun(false); _log.Information("[OpcUa] Method: Stop()"); return Array.Empty<object>(); });
                CreateMethod(methodsFolder, "Reset", "Reset statistics and fault codes",
                    Array.Empty<Argument>(), new[] { new Argument("Result", DataTypeIds.Boolean, ValueRanks.Scalar, "True if reset successful") },
                    args => { _sharedData.ResetStatistics(); _sharedData.ClearFaultCodes(); _log.Information("[OpcUa] Method: Reset()"); return new object[] { true }; });
                CreateMethod(methodsFolder, "AcknowledgeAlarm", "Acknowledge and clear alarm status",
                    Array.Empty<Argument>(), new[] { new Argument("Result", DataTypeIds.Boolean, ValueRanks.Scalar, "True if acknowledged") },
                    args => { _sharedData.SetAlarmEnableMask((ushort)(_sharedData.GetAlarmEnableMask() & ~_sharedData.GetAlarmStatusMask())); _log.Information("[OpcUa] Method: AcknowledgeAlarm()"); return new object[] { true }; });
                CreateMethod(methodsFolder, "SetFaultCode", "Inject a fault code",
                    new[] { new Argument("Index", DataTypeIds.UInt16, ValueRanks.Scalar, "Fault code index (1-4)"), new Argument("Code", DataTypeIds.UInt16, ValueRanks.Scalar, "Fault code value") },
                    new[] { new Argument("Result", DataTypeIds.Boolean, ValueRanks.Scalar, "True if set successfully") },
                    args =>
                    {
                        try
                        {
                            var index = (ushort)args[0];
                            var code = (ushort)args[1];
                            switch (index)
                            {
                                case 1: _sharedData.SetFaultCode1(code); break;
                                case 2: _sharedData.SetFaultCode2(code); break;
                                case 3: _sharedData.SetFaultCode3(code); break;
                                case 4: _sharedData.SetFaultCode4(code); break;
                                default: return new object[] { false };
                            }
                            _log.Information("[OpcUa] Method: SetFaultCode(Index={Index}, Code={Code})", index, code);
                            return new object[] { true };
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex, "[OpcUa] SetFaultCode error");
                            return new object[] { false };
                        }
                    });
            }
        }

        private FolderState CreateRootFolder(ref IList<IReference> references)
        {
            var rootFolder = new FolderState(null)
            {
                NodeId = new NodeId("Industrial", NamespaceIndex),
                BrowseName = new QualifiedName("Industrial", NamespaceIndex),
                DisplayName = "Industrial",
                TypeDefinitionId = ObjectTypeIds.FolderType,
                Description = new LocalizedText("Industrial Data"),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None
            };

            rootFolder.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, rootFolder.NodeId));

            AddPredefinedNode(SystemContext, rootFolder);
            return rootFolder;
        }

        private FolderState CreateFolder(NodeState parent, string name, string description)
        {
            var folder = new FolderState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId(name, NamespaceIndex),
                BrowseName = name,
                DisplayName = name,
                Description = new LocalizedText(description),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None
            };

            parent.AddChild(folder);
            AddPredefinedNode(SystemContext, folder);

            return folder;
        }

        private BaseDataVariableState CreateVariable(NodeState parent, string name, string description,
            NodeId dataType, Func<object> valueGetter, bool writable = false, Action<object>? writeAction = null,
            string? engineeringUnit = null, double? rangeLow = null, double? rangeHigh = null)
        {
            var accessLevel = writable ? AccessLevels.CurrentReadOrWrite : AccessLevels.CurrentRead;

            // NodeId 使用 "FolderName_VariableName" 避免跨文件夹冲突
            var nodeIdStr = $"{(parent as FolderState)?.SymbolicName}_{name}";

            var variable = new BaseDataVariableState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                NodeId = new NodeId(nodeIdStr, NamespaceIndex),
                BrowseName = name,
                DisplayName = name,
                Description = new LocalizedText(description),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                DataType = dataType,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = accessLevel,
                UserAccessLevel = accessLevel,
                Historizing = false,
                Value = valueGetter(),
                Timestamp = DateTime.UtcNow
            };

            if (writable && writeAction != null)
            {
                variable.OnWriteValue = (ISystemContext ctx, NodeState node, NumericRange indexRange,
                    QualifiedName browseName, ref object value, ref StatusCode statusCode, ref DateTime sourceTimestamp) =>
                {
                    try
                    {
                        writeAction(value);
                        _log.Information("[OpcUa] Write: {Name} = {Value}", name, value);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "[OpcUa] Write error: {Name}", name);
                        return StatusCodes.Bad;
                    }
                    return StatusCodes.Good;
                };
            }

            // EngineeringUnits 属性
            if (engineeringUnit != null)
            {
                var euProperty = new PropertyState<EUInformation>(variable)
                {
                    NodeId = new NodeId($"{nodeIdStr}_EngineeringUnits", NamespaceIndex),
                    BrowseName = BrowseNames.EngineeringUnits,
                    DisplayName = BrowseNames.EngineeringUnits,
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    DataType = DataTypeIds.EUInformation,
                    ValueRank = ValueRanks.Scalar,
                    Value = new EUInformation(engineeringUnit, engineeringUnit, "http://amvirtualslave.org")
                };
                variable.AddChild(euProperty);
            }

            // EURange 属性
            if (rangeLow.HasValue && rangeHigh.HasValue)
            {
                var rangeProperty = new PropertyState<Opc.Ua.Range>(variable)
                {
                    NodeId = new NodeId($"{nodeIdStr}_EURange", NamespaceIndex),
                    BrowseName = BrowseNames.EURange,
                    DisplayName = BrowseNames.EURange,
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    DataType = DataTypeIds.Range,
                    ValueRank = ValueRanks.Scalar,
                    Value = new Opc.Ua.Range(rangeHigh.Value, rangeLow.Value)
                };
                variable.AddChild(rangeProperty);
            }

            parent.AddChild(variable);
            _variables.Add(variable);

            AddPredefinedNode(SystemContext, variable);

            return variable;
        }

        private MethodState CreateMethod(NodeState parent, string name, string description,
            Argument[] inputArguments, Argument[] outputArguments, Func<object[], object[]> handler)
        {
            var method = new MethodState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                NodeId = new NodeId($"Methods_{name}", NamespaceIndex),
                BrowseName = name,
                DisplayName = name,
                Description = new LocalizedText(description),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                Executable = true,
                UserExecutable = true
            };

            if (inputArguments.Length > 0)
            {
                var inputArgsProperty = new PropertyState<Argument[]>(method)
                {
                    NodeId = new NodeId($"Methods_{name}_InputArgs", NamespaceIndex),
                    BrowseName = BrowseNames.InputArguments,
                    DisplayName = BrowseNames.InputArguments,
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    DataType = DataTypeIds.Argument,
                    ValueRank = ValueRanks.OneDimension,
                    ArrayDimensions = new ReadOnlyList<uint>(new uint[] { 0 }),
                    Value = inputArguments
                };
                method.AddChild(inputArgsProperty);
            }

            if (outputArguments.Length > 0)
            {
                var outputArgsProperty = new PropertyState<Argument[]>(method)
                {
                    NodeId = new NodeId($"Methods_{name}_OutputArgs", NamespaceIndex),
                    BrowseName = BrowseNames.OutputArguments,
                    DisplayName = BrowseNames.OutputArguments,
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    DataType = DataTypeIds.Argument,
                    ValueRank = ValueRanks.OneDimension,
                    ArrayDimensions = new ReadOnlyList<uint>(new uint[] { 0 }),
                    Value = outputArguments
                };
                method.AddChild(outputArgsProperty);
            }

            method.OnCallMethod = (ISystemContext ctx, MethodState methodToCall, IList<object> inputArgs, IList<object> outputArgs) =>
            {
                try
                {
                    var inputArray = new object[inputArgs.Count];
                    for (int i = 0; i < inputArgs.Count; i++) inputArray[i] = inputArgs[i];

                    var results = handler(inputArray);

                    for (int i = 0; i < results.Length && i < outputArgs.Count; i++)
                        outputArgs[i] = results[i];

                    return StatusCodes.Good;
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "[OpcUa] Method call error: {Name}", name);
                    return StatusCodes.BadInternalError;
                }
            };

            parent.AddChild(method);
            AddPredefinedNode(SystemContext, method);

            return method;
        }

        /// <summary>
        /// 通知数据变化，触发 Subscription 推送
        /// </summary>
        public void NotifyDataChanged()
        {
            lock (Lock)
            {
                var snap = _sharedData.SensorSnapshot();

                foreach (var variable in _variables)
                {
                    try
                    {
                        variable.Value = variable.SymbolicName switch
                        {
                            // Sensors
                            "Temperature" => (double)snap.Temperature,
                            "Pressure" => (double)snap.Pressure,
                            "FlowRate" => (double)snap.FlowRate,
                            "Level" => (double)snap.Level,
                            "Humidity" => (double)snap.Humidity,
                            "Rpm" => snap.Rpm,
                            "Voltage" => (double)snap.Voltage,
                            "Current" => (double)snap.Current,
                            "Power" => (double)snap.Power,
                            "Frequency" => (double)snap.Frequency,
                            // Parameters
                            "Running" => _sharedData.GetRunning(),
                            "Mode" => (int)_sharedData.GetMode(),
                            "NoiseMultiplier" => _sharedData.GetNoiseMultiplier(),
                            "ResponseDelayMs" => _sharedData.GetResponseDelayMs(),
                            "SamplePeriod" => _sharedData.GetSamplePeriod(),
                            "AlarmHighLimit" => _sharedData.GetAlarmHighLimit(),
                            "AlarmLowLimit" => _sharedData.GetAlarmLowLimit(),
                            "PowerFactor" => _sharedData.GetPowerFactor(),
                            // Statistics
                            "TempMax" => (double)_sharedData.GetTempMaxReg() / 10.0,
                            "TempMin" => (double)_sharedData.GetTempMinReg() / 10.0,
                            "TempAvg" => (double)_sharedData.GetTempAvgReg() / 10.0,
                            "PressMax" => (double)_sharedData.GetPressMaxReg() / 10.0,
                            "PressMin" => (double)_sharedData.GetPressMinReg() / 10.0,
                            "PressAvg" => (double)_sharedData.GetPressAvgReg() / 10.0,
                            "RunHours" => (int)_sharedData.GetRunHoursReg(),
                            "StartCount" => (int)_sharedData.GetStartCountReg(),
                            "CommCount" => (int)_sharedData.GetCommCountReg(),
                            "ErrorCount" => (int)_sharedData.GetErrorCountReg(),
                            // Alarms
                            "AlarmEnableMask" => _sharedData.GetAlarmEnableMask(),
                            "AlarmStatusMask" => _sharedData.GetAlarmStatusMask(),
                            "FaultCode1" => _sharedData.GetFaultCode1(),
                            "FaultCode2" => _sharedData.GetFaultCode2(),
                            "FaultCode3" => _sharedData.GetFaultCode3(),
                            "FaultCode4" => _sharedData.GetFaultCode4(),
                            "DiRunning" => _sharedData.GetDiRunning(),
                            "DiAlarm" => _sharedData.GetDiAlarm(),
                            "DiTempHigh" => _sharedData.GetDiTempHigh(),
                            "DiTempLow" => _sharedData.GetDiTempLow(),
                            "DiCommOk" => _sharedData.GetDiCommOk(),
                            "DiReady" => _sharedData.GetDiReady(),
                            _ => variable.Value
                        };

                        variable.Timestamp = DateTime.UtcNow;
                        variable.StatusCode = StatusCodes.Good;
                        variable.ClearChangeMasks(SystemContext, true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OpcUa] Error updating variable: {ex.Message}");
                    }
                }
            }
        }
    }
}
