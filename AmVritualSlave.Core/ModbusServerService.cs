using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modbus.Data;
using Modbus.Device;
using Serilog;
using System;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AmVritualSlave.Core
{
    /// <summary>
    /// Modbus 服务器服务：支持 TCP 和 RTU 串口模式，响应上位机读写请求，支持配置热重载。
    /// </summary>
    public class ModbusServerService : BackgroundService
    {
        private readonly SharedData _sharedData;
        private readonly IOptionsMonitor<AppSettings> _optionsMonitor;
        private ModbusSettings _currentModbusConfig;
        private readonly ILogger _log;
        private CancellationTokenSource? _restartCts;

        public ModbusServerService(
            SharedData sharedData,
            IOptionsMonitor<AppSettings> optionsMonitor)
        {
            _sharedData = sharedData;
            _optionsMonitor = optionsMonitor;
            _currentModbusConfig = optionsMonitor.CurrentValue.Modbus;
            _log = Log.ForContext("SourceContext", "ModbusServer");

            _optionsMonitor.OnChange(appSettings =>
            {
                var newConfig = appSettings.Modbus;

                if (newConfig.Enabled != _currentModbusConfig.Enabled)
                {
                    _log.Information("[HotReload] Modbus Enabled changed: {Old} -> {New}, triggering restart...",
                        _currentModbusConfig.Enabled, newConfig.Enabled);
                    _restartCts?.Cancel();
                }
                else
                {
                    bool criticalChanged = newConfig.Mode != _currentModbusConfig.Mode ||
                        newConfig.Port != _currentModbusConfig.Port ||
                        newConfig.SlaveId != _currentModbusConfig.SlaveId ||
                        newConfig.IpAddress != _currentModbusConfig.IpAddress ||
                        !SerialPortEquals(newConfig.SerialPort, _currentModbusConfig.SerialPort);

                    if (criticalChanged)
                    {
                        _log.Information("[HotReload] Critical config changed, triggering server restart...");
                        _restartCts?.Cancel();
                    }
                }

                _currentModbusConfig = newConfig;
                _log.Information("[HotReload] Modbus config updated: Mode={Mode}, Port={Port}, SlaveId={SlaveId}, Ip={Ip}",
                    newConfig.Mode, newConfig.Port, newConfig.SlaveId, newConfig.IpAddress);
            });
        }

        private static bool SerialPortEquals(SerialPortSettings a, SerialPortSettings b)
        {
            return a.PortName == b.PortName &&
                   a.BaudRate == b.BaudRate &&
                   a.Parity == b.Parity &&
                   a.DataBits == b.DataBits &&
                   a.StopBits == b.StopBits;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_currentModbusConfig.Enabled)
                {
                    _log.Information("[Modbus] Server is disabled in configuration, waiting for enable...");
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

                await RunServerAsync(linkedCts.Token);

                if (stoppingToken.IsCancellationRequested)
                {
                    _log.Information("Server shutdown requested.");
                    break;
                }

                _log.Information("Server restarting with new configuration...");
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
            var config = _currentModbusConfig;
            var dataStore = CreateDataStore();

            if (config.Mode.Equals("Rtu", StringComparison.OrdinalIgnoreCase))
            {
                await RunRtuServerAsync(config, dataStore, cancellationToken);
            }
            else
            {
                await RunTcpServerAsync(config, dataStore, cancellationToken);
            }
        }

        /// <summary>
        /// TCP 模式：使用 TcpListener + ModbusTcpSlave
        /// </summary>
        private async Task RunTcpServerAsync(ModbusSettings config, DataStore dataStore, CancellationToken cancellationToken)
        {
            IPAddress ipAddress;
            try
            {
                ipAddress = IPAddress.Parse(config.IpAddress);
            }
            catch (FormatException ex)
            {
                _log.Error(ex, "Invalid IP address format: {IpAddress}", config.IpAddress);
                return;
            }

            var endpoint = new IPEndPoint(ipAddress, config.Port);
            var listener = new TcpListener(endpoint);

            try
            {
                listener.Start();
                _log.Information("[Modbus/TCP] Listening on {Endpoint}", endpoint);
            }
            catch (SocketException ex)
            {
                _log.Error(ex, "Failed to start listener on {Endpoint}. Port may be in use.", endpoint);
                return;
            }

            try
            {
                var slave = ModbusTcpSlave.CreateTcp(config.SlaveId, listener);
                slave.DataStore = dataStore;
                _log.Information("[Modbus/TCP] Server started with SlaveId={SlaveId}", config.SlaveId);

                var listenTask = Task.Run(async () =>
                {
                    try { await slave.ListenAsync(); }
                    catch (OperationCanceledException) { }
                    catch (ObjectDisposedException) { }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted) { }
                    catch (Exception ex) { _log.Error(ex, "Listen loop error"); }
                }, cancellationToken);

                await Task.WhenAny(listenTask, Task.Delay(-1, cancellationToken));

                if (cancellationToken.IsCancellationRequested)
                {
                    _log.Information("[Modbus/TCP] Server stopping (cancellation requested)...");
                }

                slave?.Dispose();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[Modbus/TCP] Server error");
            }
            finally
            {
                listener.Stop();
                _log.Information("[Modbus/TCP] Server stopped on {Endpoint}", endpoint);
            }
        }

        /// <summary>
        /// RTU 模式：使用 SerialPort + ModbusSerialSlave
        /// </summary>
        private async Task RunRtuServerAsync(ModbusSettings config, DataStore dataStore, CancellationToken cancellationToken)
        {
            var sp = config.SerialPort;
            SerialPort serialPort;
            try
            {
                serialPort = new SerialPort(sp.PortName, sp.BaudRate,
                    Enum.Parse<Parity>(sp.Parity, true),
                    sp.DataBits,
                    Enum.Parse<StopBits>(sp.StopBits, true));
                serialPort.Open();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[Modbus/RTU] Failed to open serial port {PortName}", sp.PortName);
                return;
            }

            try
            {
                var slave = ModbusSerialSlave.CreateRtu(config.SlaveId, serialPort);
                slave.DataStore = dataStore;
                _log.Information("[Modbus/RTU] Server started on {PortName} @ {BaudRate}, SlaveId={SlaveId}",
                    sp.PortName, sp.BaudRate, config.SlaveId);

                var listenTask = Task.Run(async () =>
                {
                    try { await slave.ListenAsync(); }
                    catch (OperationCanceledException) { }
                    catch (ObjectDisposedException) { }
                    catch (Exception ex) { _log.Error(ex, "[Modbus/RTU] Listen loop error"); }
                }, cancellationToken);

                await Task.WhenAny(listenTask, Task.Delay(-1, cancellationToken));

                if (cancellationToken.IsCancellationRequested)
                {
                    _log.Information("[Modbus/RTU] Server stopping (cancellation requested)...");
                }

                slave?.Dispose();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[Modbus/RTU] Server error");
            }
            finally
            {
                try
                {
                    if (serialPort.IsOpen)
                        serialPort.Close();
                    serialPort.Dispose();
                }
                catch { }
                _log.Information("[Modbus/RTU] Serial port {PortName} closed", sp.PortName);
            }
        }

        /// <summary>
        /// 创建 DataStore 并绑定读写事件（TCP/RTU 共用）
        /// 使用查找表替代 if-else 链，提升可维护性
        /// </summary>
        private DataStore CreateDataStore()
        {
            // 创建足够大的 DataStore（NModbus 索引从1开始，需要 addr+1）
            var dataStore = DataStoreFactory.CreateDefaultDataStore();
            // 确保容量足够：Holding 200, Input 20, Discrete 10, Coil 10
            while (dataStore.HoldingRegisters.Count < 201) dataStore.HoldingRegisters.Add(0);
            while (dataStore.InputRegisters.Count < 21) dataStore.InputRegisters.Add(0);
            while (dataStore.InputDiscretes.Count < 11) dataStore.InputDiscretes.Add(false);
            while (dataStore.CoilDiscretes.Count < 11) dataStore.CoilDiscretes.Add(false);

            // --- Holding Register 读取查找表 (地址 → 取值函数) ---
            var holdingReaders = new System.Collections.Generic.Dictionary<ushort, Func<ushort>>
            {
                // 传感器采集值 (0-9)
                { RegisterMap.Temperature, () => _sharedData.GetTempReg() },
                { RegisterMap.Pressure, () => _sharedData.GetPressReg() },
                { RegisterMap.FlowRate, () => _sharedData.GetFlowRateReg() },
                { RegisterMap.Level, () => _sharedData.GetLevelReg() },
                { RegisterMap.Humidity, () => _sharedData.GetHumidityReg() },
                { RegisterMap.Rpm, () => _sharedData.GetRpmReg() },
                { RegisterMap.Voltage, () => _sharedData.GetVoltageReg() },
                { RegisterMap.Current, () => _sharedData.GetCurrentReg() },
                { RegisterMap.Power, () => _sharedData.GetPowerReg() },
                { RegisterMap.Frequency, () => _sharedData.GetFrequencyReg() },
                // 设备参数 (10-19)
                { RegisterMap.SimulationMode, () => (ushort)_sharedData.GetMode() },
                { RegisterMap.NoiseMultiplier, () => ClampUShort(_sharedData.GetNoiseMultiplier() * 100f) },
                { RegisterMap.ResponseDelayMs, () => ClampUShort(_sharedData.GetResponseDelayMs()) },
                { RegisterMap.SamplePeriod, () => ClampUShort(_sharedData.GetSamplePeriod()) },
                { RegisterMap.AlarmHighLimit, () => ClampUShort(_sharedData.GetAlarmHighLimit() * 10f) },
                { RegisterMap.AlarmLowLimit, () => ClampUShort(_sharedData.GetAlarmLowLimit() * 10f) },
                { RegisterMap.DeviceAddress, () => _sharedData.GetDeviceAddress() },
                { RegisterMap.BaudRateCode, () => ClampUShort(_sharedData.GetBaudRateCode()) },
                { RegisterMap.PowerFactor, () => ClampUShort(_sharedData.GetPowerFactor() * 100f) },
                // 统计数据 (20-29)
                { RegisterMap.TempMax, () => _sharedData.GetTempMaxReg() },
                { RegisterMap.TempMin, () => _sharedData.GetTempMinReg() },
                { RegisterMap.TempAvg, () => _sharedData.GetTempAvgReg() },
                { RegisterMap.PressMax, () => _sharedData.GetPressMaxReg() },
                { RegisterMap.PressMin, () => _sharedData.GetPressMinReg() },
                { RegisterMap.PressAvg, () => _sharedData.GetPressAvgReg() },
                { RegisterMap.RunHours, () => _sharedData.GetRunHoursReg() },
                { RegisterMap.StartCount, () => _sharedData.GetStartCountReg() },
                { RegisterMap.CommCount, () => _sharedData.GetCommCountReg() },
                { RegisterMap.ErrorCount, () => _sharedData.GetErrorCountReg() },
                // 报警/故障 (100-106)
                { RegisterMap.FaultInjectionControl, () => 0 }, // 只写，读回0
                { RegisterMap.AlarmEnableMask, () => _sharedData.GetAlarmEnableMask() },
                { RegisterMap.AlarmStatusMask, () => _sharedData.GetAlarmStatusMask() },
                { RegisterMap.FaultCode1, () => _sharedData.GetFaultCode1() },
                { RegisterMap.FaultCode2, () => _sharedData.GetFaultCode2() },
                { RegisterMap.FaultCode3, () => _sharedData.GetFaultCode3() },
                { RegisterMap.FaultCode4, () => _sharedData.GetFaultCode4() },
            };

            // --- Input Register 读取查找表 (地址 → 取值函数，镜像 Holding 0-9) ---
            var inputReaders = new System.Collections.Generic.Dictionary<ushort, Func<ushort>>
            {
                { RegisterMap.InputTemperature, () => _sharedData.GetTempReg() },
                { RegisterMap.InputPressure, () => _sharedData.GetPressReg() },
                { RegisterMap.InputFlowRate, () => _sharedData.GetFlowRateReg() },
                { RegisterMap.InputLevel, () => _sharedData.GetLevelReg() },
                { RegisterMap.InputHumidity, () => _sharedData.GetHumidityReg() },
                { RegisterMap.InputRpm, () => _sharedData.GetRpmReg() },
                { RegisterMap.InputVoltage, () => _sharedData.GetVoltageReg() },
                { RegisterMap.InputCurrent, () => _sharedData.GetCurrentReg() },
                { RegisterMap.InputPower, () => _sharedData.GetPowerReg() },
                { RegisterMap.InputFrequency, () => _sharedData.GetFrequencyReg() },
            };

            // --- Discrete Input 读取查找表 ---
            var discreteReaders = new System.Collections.Generic.Dictionary<ushort, Func<bool>>
            {
                { RegisterMap.DiRunning, () => _sharedData.GetDiRunning() },
                { RegisterMap.DiAlarm, () => _sharedData.GetDiAlarm() },
                { RegisterMap.DiTempHigh, () => _sharedData.GetDiTempHigh() },
                { RegisterMap.DiTempLow, () => _sharedData.GetDiTempLow() },
                { RegisterMap.DiCommOk, () => _sharedData.GetDiCommOk() },
                { RegisterMap.DiLocalMode, () => _sharedData.GetDiLocalMode() },
                { RegisterMap.DiReady, () => _sharedData.GetDiReady() },
            };

            // --- Coil 读取查找表 ---
            var coilReaders = new System.Collections.Generic.Dictionary<ushort, Func<bool>>
            {
                { RegisterMap.CoilRun, () => _sharedData.GetCoilRun() },
                { RegisterMap.CoilAlarmAck, () => _sharedData.GetCoilAlarmAck() },
                { RegisterMap.CoilFaultReset, () => _sharedData.GetCoilFaultReset() },
                { RegisterMap.CoilEnable1, () => _sharedData.GetCoilEnable1() },
                { RegisterMap.CoilEnable2, () => _sharedData.GetCoilEnable2() },
                { RegisterMap.CoilEnable3, () => _sharedData.GetCoilEnable3() },
                { RegisterMap.CoilEnable4, () => _sharedData.GetCoilEnable4() },
            };

            // ========================================================================
            // 读取事件
            // ========================================================================
            dataStore.DataStoreReadFrom += (obj, e) =>
            {
                if (obj is not DataStore store) return;

                if (e.ModbusDataType == ModbusDataType.HoldingRegister)
                {
                    for (int i = 0; i < e.Data.B.Count; i++)
                    {
                        ushort addr = (ushort)(e.StartAddress + i);
                        if (holdingReaders.TryGetValue(addr, out var reader))
                            store.HoldingRegisters[addr + 1] = reader();
                    }
                }
                else if (e.ModbusDataType == ModbusDataType.InputRegister)
                {
                    for (int i = 0; i < e.Data.B.Count; i++)
                    {
                        ushort addr = (ushort)(e.StartAddress + i);
                        if (inputReaders.TryGetValue(addr, out var reader))
                            store.InputRegisters[addr + 1] = reader();
                    }
                }
                else if (e.ModbusDataType == ModbusDataType.Input)
                {
                    for (int i = 0; i < e.Data.A.Count; i++)
                    {
                        ushort addr = (ushort)(e.StartAddress + i);
                        if (discreteReaders.TryGetValue(addr, out var reader))
                            store.InputDiscretes[addr + 1] = reader();
                    }
                }
                else if (e.ModbusDataType == ModbusDataType.Coil)
                {
                    for (int i = 0; i < e.Data.A.Count; i++)
                    {
                        ushort addr = (ushort)(e.StartAddress + i);
                        if (coilReaders.TryGetValue(addr, out var reader))
                            store.CoilDiscretes[addr + 1] = reader();
                    }
                }
            };

            // ========================================================================
            // 写入事件
            // ========================================================================
            dataStore.DataStoreWrittenTo += (obj, e) =>
            {
                _log.Information("Write request: Type={Type}, StartAddr={Addr}, Count={Count}",
                    e.ModbusDataType, e.StartAddress, e.ModbusDataType == ModbusDataType.Coil ? e.Data.A.Count : e.Data.B.Count);

                if (e.ModbusDataType == ModbusDataType.HoldingRegister)
                {
                    for (int i = 0; i < e.Data.B.Count; i++)
                    {
                        ushort addr = (ushort)(e.StartAddress + i + 1); // NModbus 写入时 addr 已经+1
                        ushort val = e.Data.B[i];
                        HandleHoldingWrite(addr, val);
                    }
                }
                else if (e.ModbusDataType == ModbusDataType.Coil)
                {
                    for (int i = 0; i < e.Data.A.Count; i++)
                    {
                        ushort addr = (ushort)(e.StartAddress + i + 1);
                        bool val = e.Data.A[i];
                        HandleCoilWrite(addr, val);
                    }
                }

                _sharedData.IncrementCommCount();
            };

            return dataStore;
        }

        /// <summary>
        /// 处理 Holding Register 写入
        /// </summary>
        private void HandleHoldingWrite(ushort addr, ushort val)
        {
            switch (addr)
            {
                // 设备参数 (10-19)
                case RegisterMap.SimulationMode:
                    if (Enum.IsDefined(typeof(SharedData.SimulationMode), val))
                        _sharedData.SetMode((SharedData.SimulationMode)val);
                    break;
                case RegisterMap.NoiseMultiplier:
                    _sharedData.SetNoiseMultiplier(val / 100f);
                    break;
                case RegisterMap.ResponseDelayMs:
                    _sharedData.SetResponseDelay(val);
                    break;
                case RegisterMap.SamplePeriod:
                    _sharedData.SetSamplePeriod(val);
                    break;
                case RegisterMap.AlarmHighLimit:
                    _sharedData.SetAlarmHighLimit(val / 10f);
                    break;
                case RegisterMap.AlarmLowLimit:
                    _sharedData.SetAlarmLowLimit(val / 10f);
                    break;
                case RegisterMap.DeviceAddress:
                    _sharedData.SetDeviceAddress((byte)val);
                    break;
                case RegisterMap.BaudRateCode:
                    _sharedData.SetBaudRateCode(val);
                    break;
                case RegisterMap.PowerFactor:
                    _sharedData.SetPowerFactor(val / 100f);
                    break;

                // 报警/故障 (100-106)
                case RegisterMap.FaultInjectionControl:
                    switch (val)
                    {
                        case 0:
                            _sharedData.ResumeNormal();
                            _log.Information("[FaultInjection] Resumed normal operation");
                            break;
                        case 1:
                            _sharedData.InjectFaultyTemperature();
                            _log.Warning("[FaultInjection] Injected faulty temperature (999.9°C)");
                            break;
                        case 2:
                            _sharedData.InjectFaultyPressure();
                            _log.Warning("[FaultInjection] Injected faulty pressure (-50.0 kPa)");
                            break;
                        case 3:
                            _sharedData.FreezeData();
                            _log.Warning("[FaultInjection] Frozen data updates");
                            break;
                        default:
                            _sharedData.IncrementErrorCount();
                            _log.Warning("[FaultInjection] Unknown control code: {Code}", val);
                            break;
                    }
                    break;
                case RegisterMap.AlarmEnableMask:
                    _sharedData.SetAlarmEnableMask(val);
                    break;
                case RegisterMap.FaultCode1:
                    _sharedData.SetFaultCode1(val);
                    break;
                case RegisterMap.FaultCode2:
                    _sharedData.SetFaultCode2(val);
                    break;
                case RegisterMap.FaultCode3:
                    _sharedData.SetFaultCode3(val);
                    break;
                case RegisterMap.FaultCode4:
                    _sharedData.SetFaultCode4(val);
                    break;

                default:
                    _log.Debug("Write to unmapped Holding address {Addr} = {Value}", addr, val);
                    break;
            }
        }

        /// <summary>
        /// 处理 Coil 写入
        /// </summary>
        private void HandleCoilWrite(ushort addr, bool val)
        {
            switch (addr)
            {
                case RegisterMap.CoilRun:
                    _sharedData.SetCoilRun(val);
                    break;
                case RegisterMap.CoilAlarmAck:
                    _sharedData.SetCoilAlarmAck(val);
                    break;
                case RegisterMap.CoilFaultReset:
                    _sharedData.SetCoilFaultReset(val);
                    break;
                case RegisterMap.CoilEnable1:
                    _sharedData.SetCoilEnable1(val);
                    break;
                case RegisterMap.CoilEnable2:
                    _sharedData.SetCoilEnable2(val);
                    break;
                case RegisterMap.CoilEnable3:
                    _sharedData.SetCoilEnable3(val);
                    break;
                case RegisterMap.CoilEnable4:
                    _sharedData.SetCoilEnable4(val);
                    break;
                default:
                    _log.Debug("Write to unmapped Coil address {Addr} = {Value}", addr, val);
                    break;
            }
        }

        private static ushort ClampUShort(double val)
        {
            if (val < 0) return 0;
            if (val > 65535) return 65535;
            return (ushort)val;
        }

        private static ushort ClampUShort(int val)
        {
            if (val < 0) return 0;
            if (val > 65535) return 65535;
            return (ushort)val;
        }
    }
}
