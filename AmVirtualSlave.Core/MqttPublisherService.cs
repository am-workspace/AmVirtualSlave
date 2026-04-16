using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AmVirtualSlave.Core
{
    /// <summary>
    /// MQTT Publisher 服务：将 SharedData 数据发布到 MQTT Broker，支持配置热重载。
    /// </summary>
    public class MqttPublisherService : BackgroundService
    {
        private readonly SharedData _sharedData;
        private readonly IOptionsMonitor<AppSettings> _optionsMonitor;
        private MqttSettings _currentMqttConfig;
        private readonly ILogger _log;
        private IMqttClient? _mqttClient;
        private MqttClientOptions? _mqttOptions;
        private CancellationTokenSource? _restartCts;

        public MqttPublisherService(
            SharedData sharedData,
            IOptionsMonitor<AppSettings> optionsMonitor)
        {
            _sharedData = sharedData;
            _optionsMonitor = optionsMonitor;
            _currentMqttConfig = optionsMonitor.CurrentValue.Mqtt;
            _log = Log.ForContext("SourceContext", "MqttPublisher");

            // 配置热重载：关键配置变更时触发重连
            _optionsMonitor.OnChange(appSettings =>
            {
                var newConfig = appSettings.Mqtt;

                // Enabled 开关变更
                if (newConfig.Enabled != _currentMqttConfig.Enabled)
                {
                    _log.Information("[HotReload] Mqtt Enabled changed: {Old} -> {New}, triggering restart...",
                        _currentMqttConfig.Enabled, newConfig.Enabled);
                    _restartCts?.Cancel();
                }
                // 连接参数变更（需要重连的配置）
                else if (newConfig.Enabled && _currentMqttConfig.Enabled &&
                    (newConfig.Broker != _currentMqttConfig.Broker ||
                     newConfig.Port != _currentMqttConfig.Port ||
                     newConfig.Username != _currentMqttConfig.Username ||
                     newConfig.Password != _currentMqttConfig.Password))
                {
                    _log.Information("[HotReload] Connection config changed (Broker/Port/Auth), triggering reconnect...");
                    _restartCts?.Cancel();
                }

                _currentMqttConfig = newConfig;
                _log.Information("[HotReload] Mqtt config updated: Enabled={Enabled}, Broker={Broker}, Port={Port}",
                    newConfig.Enabled, newConfig.Broker, newConfig.Port);
            });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 外层循环：支持配置变更后自动重连
            while (!stoppingToken.IsCancellationRequested)
            {
                // 检查是否启用
                if (!_currentMqttConfig.Enabled)
                {
                    _log.Information("[Mqtt] Publisher is disabled in configuration, waiting for enable...");
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
                    await RunPublisherAsync(linkedCts.Token);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "[Mqtt] Publisher error");
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    _log.Information("[Mqtt] Publisher shutdown requested.");
                    break;
                }

                // 配置变更触发的重连
                _log.Information("[Mqtt] Publisher restarting with new configuration...");
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

        /// <summary>
        /// 连接 Broker 并发布数据，支持断线自动重连（指数退避）。
        /// </summary>
        private async Task RunPublisherAsync(CancellationToken cancellationToken)
        {
            await ConnectAsync();

            // 注册断线事件：Broker 非正常断连时触发重连
            _mqttClient!.DisconnectedAsync += OnMqttDisconnected;

            _sharedData.DataChanged += OnDataChanged;

            try
            {
                var tcs = new TaskCompletionSource();
                using var registration = cancellationToken.Register(() =>
                {
                    tcs.TrySetResult();
                });

                await tcs.Task;
            }
            finally
            {
                _sharedData.DataChanged -= OnDataChanged;
                if (_mqttClient != null)
                {
                    _mqttClient.DisconnectedAsync -= OnMqttDisconnected;
                }
                await DisconnectAsync();
                _log.Information("[Mqtt] Publisher stopped");
            }
        }

        /// <summary>
        /// MQTT 断线事件处理：指数退避自动重连。
        /// </summary>
        private async Task OnMqttDisconnected(MqttClientDisconnectedEventArgs args)
        {
            if (args.ClientWasConnected)
            {
                _log.Warning("[Mqtt] Connection lost, starting reconnect with backoff...");
            }

            int retryDelayMs = 1000;   // 初始 1s
            const int maxDelayMs = 30000; // 最大 30s

            while (!IsDisposed() && _mqttClient != null && !_mqttClient.IsConnected)
            {
                try
                {
                    _log.Information("[Mqtt] Reconnecting in {Delay}ms...", retryDelayMs);
                    await Task.Delay(retryDelayMs);

                    // 用当前最新配置重建连接选项
                    var settings = _currentMqttConfig;
                    var optionsBuilder = new MqttClientOptionsBuilder()
                        .WithTcpServer(settings.Broker, settings.Port)
                        .WithClientId($"AmVirtualSlave_{Guid.NewGuid():N}")
                        .WithCleanSession();

                    if (!string.IsNullOrEmpty(settings.Username))
                    {
                        optionsBuilder.WithCredentials(settings.Username, settings.Password ?? "");
                    }

                    _mqttOptions = optionsBuilder.Build();
                    await _mqttClient.ConnectAsync(_mqttOptions);

                    _log.Information("[Mqtt] Reconnected to broker {Broker}:{Port}", settings.Broker, settings.Port);
                    return; // 重连成功，退出重试循环
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[Mqtt] Reconnect failed, retrying...");
                    retryDelayMs = Math.Min(retryDelayMs * 2, maxDelayMs);
                }
            }
        }

        private bool IsDisposed()
        {
            try { _ = _mqttClient?.IsConnected; return false; }
            catch { return true; }
        }

        private async Task ConnectAsync()
        {
            var settings = _currentMqttConfig;
            var factory = new MqttClientFactory();
            _mqttClient = factory.CreateMqttClient();

            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(settings.Broker, settings.Port)
                .WithClientId($"AmVirtualSlave_{Guid.NewGuid():N}")
                .WithCleanSession();

            if (!string.IsNullOrEmpty(settings.Username))
            {
                optionsBuilder.WithCredentials(settings.Username, settings.Password ?? "");
                _log.Information("[Mqtt] Using authentication with username: {Username}", settings.Username);
            }

            _mqttOptions = optionsBuilder.Build();

            await _mqttClient.ConnectAsync(_mqttOptions);
            _log.Information("[Mqtt] Connected to broker {Broker}:{Port}", settings.Broker, settings.Port);
        }

        private async Task DisconnectAsync()
        {
            if (_mqttClient?.IsConnected == true)
            {
                await _mqttClient.DisconnectAsync();
                _log.Information("[Mqtt] Disconnected from broker");
            }
            _mqttClient?.Dispose();
            _mqttClient = null;
        }

        private void OnDataChanged(object? sender, DataChangedEventArgs e)
        {
            _ = PublishDataAsync(e);
        }

        private async Task PublishDataAsync(DataChangedEventArgs e)
        {
            if (_mqttClient?.IsConnected != true)
            {
                _log.Warning("[Mqtt] Not connected to broker, skipping publish");
                return;
            }

            try
            {
                var timestamp = DateTime.UtcNow.ToString("O");
                var sensorId = "sensor001";
                var topicPrefix = _currentMqttConfig.TopicPrefix;

                // 发布所有传感器数据（单条合并消息，减少 MQTT 消息数量）
                await PublishMessageAsync(
                    $"{topicPrefix}/{sensorId}/data",
                    new
                    {
                        sensorId,
                        timestamp,
                        sensors = new
                        {
                            temperature = e.Temperature,
                            pressure = e.Pressure,
                            flowRate = e.FlowRate,
                            level = e.Level,
                            humidity = e.Humidity,
                            rpm = e.Rpm,
                            voltage = e.Voltage,
                            current = e.Current,
                            power = e.Power,
                            frequency = e.Frequency
                        },
                        status = new
                        {
                            running = e.Running,
                            mode = e.Mode.ToString(),
                            alarmStatus = e.AlarmStatusMask,
                            faultCodes = new[] { e.FaultCode1, e.FaultCode2, e.FaultCode3, e.FaultCode4 }
                        }
                    });
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[Mqtt] Failed to publish data");
            }
        }

        private async Task PublishMessageAsync(string topic, object payload)
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(JsonConvert.SerializeObject(payload))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            await _mqttClient!.PublishAsync(message);
            _log.Debug("[Mqtt] Published to {Topic}: {Payload}", topic, payload);
        }
    }
}
