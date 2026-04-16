using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AmVritualSlave.Core
{
    /// <summary>
    /// 数据生成服务：后台持续产生模拟传感器数据。
    /// 电力参数互相关联：P = U × I × cosφ
    /// </summary>
    public class GeneratorService : BackgroundService
    {
        private readonly SharedData _sharedData;
        private readonly IOptionsMonitor<AppSettings> _optionsMonitor;
        private SimulationSettings _currentSimConfig;
        private readonly ITimeProvider _timeProvider;
        private readonly IRandomProvider _randomProvider;
        private readonly ILogger _log;

        // 累计运行时间追踪
        private DateTime _lastRunHourUpdate = DateTime.UtcNow;

        public GeneratorService(
            SharedData sharedData,
            IOptionsMonitor<AppSettings> optionsMonitor)
        {
            _sharedData = sharedData;
            _optionsMonitor = optionsMonitor;
            _currentSimConfig = optionsMonitor.CurrentValue.Simulation;
            _timeProvider = new TimeProvider();
            _randomProvider = new RandomProvider();
            _log = Log.ForContext("SourceContext", "Generator");

            // 配置热重载：配置变更时自动更新参数
            _optionsMonitor.OnChange(appSettings =>
            {
                _currentSimConfig = appSettings.Simulation;
                _sharedData.SetNoiseMultiplier(appSettings.Simulation.DefaultNoise);
                _sharedData.SetResponseDelay(appSettings.Simulation.DefaultDelayMs);
                if (Enum.TryParse<SharedData.SimulationMode>(appSettings.Simulation.InitialMode, out var mode))
                {
                    _sharedData.SetMode(mode);
                }
                _log.Information("[HotReload] Simulation config reloaded: Mode={Mode}, Noise={Noise}, Delay={Delay}",
                    appSettings.Simulation.InitialMode, appSettings.Simulation.DefaultNoise, appSettings.Simulation.DefaultDelayMs);
            });
        }

        /// <summary>
        /// 主循环：根据当前模式生成模拟数据并更新到 SharedData。
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var mode = _sharedData.GetMode();
                var noise = _sharedData.GetNoiseMultiplier();
                var delay = _sharedData.GetResponseDelayMs();
                int timeout = _currentSimConfig.TimeoutMs;
                int interval = _currentSimConfig.UpdateIntervalMs;

                // 冻结模式：保持数值不变
                if (mode == SharedData.SimulationMode.Frozen)
                {
                    var frozenSnap = _sharedData.Snapshot();
                    _log.Debug("Frozen state: Temp={Temp}, Press={Press}, Running={Running}", frozenSnap.Temp, frozenSnap.Press, frozenSnap.Status);
                    await _timeProvider.Delay(timeout, stoppingToken);
                    continue;
                }

                // ========================================================================
                // 生成传感器数据
                // ========================================================================

                var now = _timeProvider.UtcNow;
                var seconds = now.Second + now.Minute * 60 + now.Hour * 3600;

                // --- 温度 (20-30°C) ---
                float t = (float)(_randomProvider.NextDouble() * 10.0 + 20.0);
                if (mode == SharedData.SimulationMode.Trend)
                    t += (float)Math.Sin(seconds / 60.0 * Math.PI * 2) * 3.0f;
                t += (float)((_randomProvider.NextDouble() - 0.5) * 2.0 * noise);
                t = (float)Math.Round(t, 2);

                // --- 压力 (90-110 kPa) ---
                float p = (float)(_randomProvider.NextDouble() * 20.0 + 90.0);
                if (mode == SharedData.SimulationMode.Trend)
                    p += (float)Math.Sin(seconds / 30.0 * Math.PI * 2) * 5.0f;
                p += (float)((_randomProvider.NextDouble() - 0.5) * 2.0 * noise);
                p = (float)Math.Round(p, 2);

                // --- 流量 (20-200 m³/h) ---
                float flow = (float)(_randomProvider.NextDouble() * 180.0 + 20.0);
                if (mode == SharedData.SimulationMode.Trend)
                    flow += (float)Math.Sin(seconds / 45.0 * Math.PI * 2) * 20.0f;
                flow += (float)((_randomProvider.NextDouble() - 0.5) * 4.0 * noise);
                flow = Math.Max(0, (float)Math.Round(flow, 2));

                // --- 液位 (0-100%) ---
                float level = (float)(_randomProvider.NextDouble() * 100.0);
                if (mode == SharedData.SimulationMode.Trend)
                    level += (float)Math.Sin(seconds / 120.0 * Math.PI * 2) * 15.0f;
                level += (float)((_randomProvider.NextDouble() - 0.5) * 2.0 * noise);
                level = Math.Clamp((float)Math.Round(level, 2), 0, 100);

                // --- 湿度 (40-80 %RH) ---
                float humidity = (float)(_randomProvider.NextDouble() * 40.0 + 40.0);
                if (mode == SharedData.SimulationMode.Trend)
                    humidity += (float)Math.Sin(seconds / 180.0 * Math.PI * 2) * 8.0f;
                humidity += (float)((_randomProvider.NextDouble() - 0.5) * 2.0 * noise);
                humidity = Math.Clamp((float)Math.Round(humidity, 2), 0, 100);

                // --- 转速 (1400-1600 RPM, 阶跃变化) ---
                int rpmBase = 1500 + (int)(Math.Floor(seconds / 10.0) % 3) * 50; // 每10秒阶跃
                int rpm = rpmBase + (int)((_randomProvider.NextDouble() - 0.5) * 20 * noise);
                rpm = Math.Max(0, rpm);

                // --- 电力参数 (互相关联: P = U × I × cosφ) ---
                float cosPhi = _sharedData.GetPowerFactor(); // 读取当前功率因数

                // 电压 (370-390 V)
                float voltage = (float)(_randomProvider.NextDouble() * 20.0 + 370.0);
                if (mode == SharedData.SimulationMode.Trend)
                    voltage += (float)Math.Sin(seconds / 20.0 * Math.PI * 2) * 5.0f;
                voltage += (float)((_randomProvider.NextDouble() - 0.5) * 2.0 * noise);
                voltage = (float)Math.Round(voltage, 2);

                // 电流 (5-15 A)
                float current = (float)(_randomProvider.NextDouble() * 10.0 + 5.0);
                if (mode == SharedData.SimulationMode.Trend)
                    current += (float)Math.Sin(seconds / 25.0 * Math.PI * 2) * 2.0f;
                current += (float)((_randomProvider.NextDouble() - 0.5) * 1.0 * noise);
                current = Math.Max(0.01f, (float)Math.Round(current, 2));

                // 功率 = U × I × cosφ (关联计算)
                float power = (float)Math.Round(voltage * current * cosPhi, 2);

                // 频率 (49.5-50.5 Hz)
                float freq = (float)(_randomProvider.NextDouble() * 1.0 + 49.5);
                freq += (float)((_randomProvider.NextDouble() - 0.5) * 0.2 * noise);
                freq = (float)Math.Round(freq, 2);

                // --- 运行状态 ---
                bool running = _sharedData.GetRunning();

                // 模拟响应延迟
                if (delay > 0)
                {
                    try { await _timeProvider.Delay(delay, stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }

                // 更新共享数据
                _sharedData.UpdateSensorData(t, p, flow, level, humidity, rpm, voltage, current, power, freq);

                // 更新运行时间统计
                UpdateRunHours();

                _log.Debug("Generated: T={T}°C P={P}kPa F={F}m³/h L={L}% H={H}%RPM RPM={RPM} U={U}V I={I}A P={P2}W f={Freq}Hz cosφ={CosPhi}",
                    t, p, flow, level, humidity, rpm, voltage, current, power, freq, cosPhi);

                await _timeProvider.Delay(interval, stoppingToken);
            }
            _log.Information("Generator task stopped.");
        }

        /// <summary>
        /// 累计运行小时数更新
        /// </summary>
        private void UpdateRunHours()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastRunHourUpdate).TotalHours >= 1.0)
            {
                // 在真实场景中每小时+1，这里为了演示加速：每10秒+1
                // TODO: 后续可通过配置调整时间加速倍率
                int elapsedSeconds = (int)(now - _lastRunHourUpdate).TotalSeconds;
                if (elapsedSeconds >= 10)
                {
                    _sharedData.IncrementRunHours();
                    _lastRunHourUpdate = now;
                }
            }
        }
    }
}
