using System;
using System.Collections.Generic;
using System.Text;

namespace AmVritualSlave.Core
{
    /// <summary>
    /// 共享数据中心：存储模拟器的所有状态（传感器数值、配置参数、统计数据、报警信息）。
    /// 关键特性：线程安全 (Thread-Safe)。
    /// </summary>
    public class SharedData
    {
        readonly object _lock = new object();

        /// <summary>
        /// 数据变更事件，供 OPC UA / MQTT 等外部订阅使用
        /// </summary>
        public event EventHandler<DataChangedEventArgs>? DataChanged;

        // ========================================================================
        // 传感器实时数据 (由 GeneratorService 写入，由 Modbus/OPC UA 读取)
        // ========================================================================

        float _temperature = 25.0f;      // °C
        float _pressure = 100.0f;         // kPa
        float _flowRate = 50.0f;          // m³/h
        float _level = 60.0f;             // %
        float _humidity = 55.0f;          // %RH
        int _rpm = 1500;                  // RPM
        float _voltage = 380.0f;          // V
        float _current = 10.0f;           // A
        float _power = 3800.0f;           // W
        float _frequency = 50.0f;         // Hz

        // ========================================================================
        // 设备状态
        // ========================================================================

        bool _running = true;             // 设备运行状态

        // ========================================================================
        // 控制配置参数 (由外部 Modbus/OPC UA 客户端写入，由 GeneratorService 读取)
        // ========================================================================

        public enum SimulationMode { Random = 0, Trend = 1, Frozen = 2 }

        SimulationMode _mode = SimulationMode.Random;
        float _noise = 1.0f;
        int _delayMs = 0;
        int _samplePeriod = 2000;         // ms, 采样周期
        float _alarmHighLimit = 30.0f;    // °C, 报警上限
        float _alarmLowLimit = 20.0f;     // °C, 报警下限
        byte _deviceAddress = 1;          // 设备地址
        int _baudRateCode = 0;            // 0=9600, 1=19200, 2=38400, 3=115200
        float _powerFactor = 0.85f;       // cosφ

        // ========================================================================
        // 统计数据 (由 GeneratorService 累积更新)
        // ========================================================================

        float _tempMax = float.MinValue;
        float _tempMin = float.MaxValue;
        float _tempAvg = 25.0f;
        float _pressMax = float.MinValue;
        float _pressMin = float.MaxValue;
        float _pressAvg = 100.0f;
        int _runHours = 0;
        int _startCount = 1;
        int _commCount = 0;
        int _errorCount = 0;

        // 内部统计辅助
        int _tempSumCount = 0;
        double _tempSum = 0;
        int _pressSumCount = 0;
        double _pressSum = 0;

        // ========================================================================
        // 报警/故障
        // ========================================================================

        ushort _alarmEnableMask = 0x0F;   // bit0-3 全部使能
        ushort _alarmStatusMask = 0;       // 自动计算
        ushort _faultCode1 = 0;
        ushort _faultCode2 = 0;
        ushort _faultCode3 = 0;
        ushort _faultCode4 = 0;

        // ========================================================================
        // 离散输入 (只读, 由 GeneratorService 根据数据自动设置)
        // ========================================================================

        bool _diRunning = true;
        bool _diAlarm = false;
        bool _diTempHigh = false;
        bool _diTempLow = false;
        bool _diCommOk = true;
        bool _diLocalMode = false;
        bool _diReady = true;

        // ========================================================================
        // 线圈 (可写)
        // ========================================================================

        bool _coilRun = true;
        bool _coilAlarmAck = false;
        bool _coilFaultReset = false;
        bool _coilEnable1 = true;
        bool _coilEnable2 = true;
        bool _coilEnable3 = true;
        bool _coilEnable4 = true;

        // ========================================================================
        // 读取操作 — 传感器数据
        // ========================================================================

        /// <summary>
        /// 获取传感器数据的完整快照
        /// </summary>
        public SensorSnapshot SensorSnapshot()
        {
            lock (_lock) return new SensorSnapshot(
                _temperature, _pressure, _flowRate, _level, _humidity,
                _rpm, _voltage, _current, _power, _frequency, _running);
        }

        /// <summary>获取温度寄存器值 (放大10倍)</summary>
        public ushort GetTempReg() { lock (_lock) return (ushort)(_temperature * 10); }
        /// <summary>获取压力寄存器值 (放大10倍)</summary>
        public ushort GetPressReg() { lock (_lock) return (ushort)(_pressure * 10); }
        /// <summary>获取流量寄存器值 (放大10倍)</summary>
        public ushort GetFlowRateReg() { lock (_lock) return ToUShort(_flowRate * 10); }
        /// <summary>获取液位寄存器值 (放大10倍)</summary>
        public ushort GetLevelReg() { lock (_lock) return ToUShort(_level * 10); }
        /// <summary>获取湿度寄存器值 (放大10倍)</summary>
        public ushort GetHumidityReg() { lock (_lock) return ToUShort(_humidity * 10); }
        /// <summary>获取转速寄存器值 (直写)</summary>
        public ushort GetRpmReg() { lock (_lock) return ToUShort(_rpm); }
        /// <summary>获取电压寄存器值 (放大10倍)</summary>
        public ushort GetVoltageReg() { lock (_lock) return ToUShort(_voltage * 10); }
        /// <summary>获取电流寄存器值 (放大100倍)</summary>
        public ushort GetCurrentReg() { lock (_lock) return ToUShort(_current * 100); }
        /// <summary>获取功率寄存器值 (放大10倍)</summary>
        public ushort GetPowerReg() { lock (_lock) return ToUShort(_power * 10); }
        /// <summary>获取频率寄存器值 (放大100倍)</summary>
        public ushort GetFrequencyReg() { lock (_lock) return ToUShort(_frequency * 100); }

        /// <summary>获取运行状态</summary>
        public bool GetRunning() { lock (_lock) return _running; }

        // ========================================================================
        // 读取操作 — 控制参数
        // ========================================================================

        public SimulationMode GetMode() { lock (_lock) return _mode; }
        public float GetNoiseMultiplier() { lock (_lock) return _noise; }
        public int GetResponseDelayMs() { lock (_lock) return _delayMs; }
        public int GetSamplePeriod() { lock (_lock) return _samplePeriod; }
        public float GetAlarmHighLimit() { lock (_lock) return _alarmHighLimit; }
        public float GetAlarmLowLimit() { lock (_lock) return _alarmLowLimit; }
        public byte GetDeviceAddress() { lock (_lock) return _deviceAddress; }
        public int GetBaudRateCode() { lock (_lock) return _baudRateCode; }
        public float GetPowerFactor() { lock (_lock) return _powerFactor; }

        // ========================================================================
        // 读取操作 — 统计数据
        // ========================================================================

        public ushort GetTempMaxReg() { lock (_lock) return ToUShort(_tempMax * 10); }
        public ushort GetTempMinReg() { lock (_lock) return ToUShort(_tempMin * 10); }
        public ushort GetTempAvgReg() { lock (_lock) return ToUShort(_tempAvg * 10); }
        public ushort GetPressMaxReg() { lock (_lock) return ToUShort(_pressMax * 10); }
        public ushort GetPressMinReg() { lock (_lock) return ToUShort(_pressMin * 10); }
        public ushort GetPressAvgReg() { lock (_lock) return ToUShort(_pressAvg * 10); }
        public ushort GetRunHoursReg() { lock (_lock) return ToUShort(_runHours); }
        public ushort GetStartCountReg() { lock (_lock) return ToUShort(_startCount); }
        public ushort GetCommCountReg() { lock (_lock) return ToUShort(_commCount); }
        public ushort GetErrorCountReg() { lock (_lock) return ToUShort(_errorCount); }

        // ========================================================================
        // 读取操作 — 报警/故障
        // ========================================================================

        public ushort GetAlarmEnableMask() { lock (_lock) return _alarmEnableMask; }
        public ushort GetAlarmStatusMask() { lock (_lock) return _alarmStatusMask; }
        public ushort GetFaultCode1() { lock (_lock) return _faultCode1; }
        public ushort GetFaultCode2() { lock (_lock) return _faultCode2; }
        public ushort GetFaultCode3() { lock (_lock) return _faultCode3; }
        public ushort GetFaultCode4() { lock (_lock) return _faultCode4; }

        // ========================================================================
        // 读取操作 — 离散输入
        // ========================================================================

        public bool GetDiRunning() { lock (_lock) return _diRunning; }
        public bool GetDiAlarm() { lock (_lock) return _diAlarm; }
        public bool GetDiTempHigh() { lock (_lock) return _diTempHigh; }
        public bool GetDiTempLow() { lock (_lock) return _diTempLow; }
        public bool GetDiCommOk() { lock (_lock) return _diCommOk; }
        public bool GetDiLocalMode() { lock (_lock) return _diLocalMode; }
        public bool GetDiReady() { lock (_lock) return _diReady; }

        /// <summary>获取离散输入数组 (按地址索引)</summary>
        public bool[] GetDiscreteInputs()
        {
            lock (_lock) return new[] { _diRunning, _diAlarm, _diTempHigh, _diTempLow, _diCommOk, _diLocalMode, _diReady, false };
        }

        // ========================================================================
        // 读取操作 — 线圈
        // ========================================================================

        public bool GetCoilRun() { lock (_lock) return _coilRun; }
        public bool GetCoilAlarmAck() { lock (_lock) return _coilAlarmAck; }
        public bool GetCoilFaultReset() { lock (_lock) return _coilFaultReset; }
        public bool GetCoilEnable1() { lock (_lock) return _coilEnable1; }
        public bool GetCoilEnable2() { lock (_lock) return _coilEnable2; }
        public bool GetCoilEnable3() { lock (_lock) return _coilEnable3; }
        public bool GetCoilEnable4() { lock (_lock) return _coilEnable4; }

        /// <summary>获取线圈数组 (按地址索引)</summary>
        public bool[] GetCoils()
        {
            lock (_lock) return new[] { _coilRun, _coilAlarmAck, _coilFaultReset, _coilEnable1, _coilEnable2, _coilEnable3, _coilEnable4, false };
        }

        // ========================================================================
        // 写入操作 — 传感器数据 (由 GeneratorService 调用)
        // ========================================================================

        /// <summary>
        /// 批量更新传感器数据（保证原子性），同时更新统计和报警
        /// </summary>
        public void UpdateSensorData(float temp, float press, float flowRate, float level,
            float humidity, int rpm, float voltage, float current, float power, float frequency)
        {
            lock (_lock)
            {
                _temperature = temp;
                _pressure = press;
                _flowRate = flowRate;
                _level = level;
                _humidity = humidity;
                _rpm = rpm;
                _voltage = voltage;
                _current = current;
                _power = power;
                _frequency = frequency;

                // 更新统计
                UpdateStats(temp, press);

                // 更新报警状态
                UpdateAlarmStatus();

                // 更新离散输入
                _diRunning = _running;
                _diTempHigh = (_alarmEnableMask & 0x01) != 0 && temp > _alarmHighLimit;
                _diTempLow = (_alarmEnableMask & 0x02) != 0 && temp < _alarmLowLimit;
                _diAlarm = _alarmStatusMask != 0;
                _diReady = _running;
            }
            FireDataChanged();
        }

        /// <summary>
        /// 触发数据变更事件（用于配置变更时通知订阅者更新）
        /// </summary>
        public void NotifyDataChanged()
        {
            FireDataChanged();
        }

        // ========================================================================
        // 写入操作 — 控制参数 (由 Modbus/OPC UA 写入)
        // ========================================================================

        public void SetRunning(bool value) { lock (_lock) { _running = value; _coilRun = value; } FireDataChanged(); }
        public void SetMode(SimulationMode m) { lock (_lock) _mode = m; FireDataChanged(); }
        public void SetNoiseMultiplier(float m) { lock (_lock) _noise = m; FireDataChanged(); }
        public void SetResponseDelay(int ms) { lock (_lock) _delayMs = ms; FireDataChanged(); }
        public void SetSamplePeriod(int ms) { lock (_lock) _samplePeriod = Math.Clamp(ms, 100, 60000); FireDataChanged(); }
        public void SetAlarmHighLimit(float val) { lock (_lock) _alarmHighLimit = val; FireDataChanged(); }
        public void SetAlarmLowLimit(float val) { lock (_lock) _alarmLowLimit = val; FireDataChanged(); }
        public void SetDeviceAddress(byte addr) { lock (_lock) _deviceAddress = Math.Clamp(addr, (byte)1, (byte)247); FireDataChanged(); }
        public void SetBaudRateCode(int code) { lock (_lock) _baudRateCode = Math.Clamp(code, 0, 3); FireDataChanged(); }
        public void SetPowerFactor(float pf) { lock (_lock) _powerFactor = Math.Clamp(pf, 0.0f, 1.0f); FireDataChanged(); }

        // ========================================================================
        // 写入操作 — 报警/故障 (由 Modbus/OPC UA 写入)
        // ========================================================================

        public void SetAlarmEnableMask(ushort mask) { lock (_lock) _alarmEnableMask = mask; FireDataChanged(); }
        public void SetFaultCode1(ushort code) { lock (_lock) _faultCode1 = code; FireDataChanged(); }
        public void SetFaultCode2(ushort code) { lock (_lock) _faultCode2 = code; FireDataChanged(); }
        public void SetFaultCode3(ushort code) { lock (_lock) _faultCode3 = code; FireDataChanged(); }
        public void SetFaultCode4(ushort code) { lock (_lock) _faultCode4 = code; FireDataChanged(); }

        // ========================================================================
        // 写入操作 — 线圈 (由 Modbus/OPC UA 写入)
        // ========================================================================

        public void SetCoilRun(bool value) { lock (_lock) { _coilRun = value; _running = value; } FireDataChanged(); }
        public void SetCoilAlarmAck(bool value) { lock (_lock) _coilAlarmAck = value; if (value) { _alarmStatusMask = 0; _diAlarm = false; } FireDataChanged(); }
        public void SetCoilFaultReset(bool value) { lock (_lock) { _coilFaultReset = value; if (value) { _faultCode1 = _faultCode2 = _faultCode3 = _faultCode4 = 0; _coilFaultReset = false; } } FireDataChanged(); }
        public void SetCoilEnable1(bool value) { lock (_lock) _coilEnable1 = value; FireDataChanged(); }
        public void SetCoilEnable2(bool value) { lock (_lock) _coilEnable2 = value; FireDataChanged(); }
        public void SetCoilEnable3(bool value) { lock (_lock) _coilEnable3 = value; FireDataChanged(); }
        public void SetCoilEnable4(bool value) { lock (_lock) _coilEnable4 = value; FireDataChanged(); }

        // ========================================================================
        // 通信计数
        // ========================================================================

        public void IncrementCommCount() { lock (_lock) _commCount++; }
        public void IncrementErrorCount() { lock (_lock) _errorCount++; }
        public void IncrementRunHours() { lock (_lock) _runHours++; }

        /// <summary>重置所有统计数据</summary>
        public void ResetStatistics()
        {
            lock (_lock)
            {
                _tempMax = 0; _tempMin = 0; _tempAvg = 0; _tempSumCount = 0; _tempSum = 0;
                _pressMax = 0; _pressMin = 0; _pressAvg = 0; _pressSumCount = 0; _pressSum = 0;
                _runHours = 0; _startCount = 0; _commCount = 0; _errorCount = 0;
            }
            FireDataChanged();
        }

        /// <summary>清除所有故障码</summary>
        public void ClearFaultCodes()
        {
            lock (_lock) { _faultCode1 = _faultCode2 = _faultCode3 = _faultCode4 = 0; }
            FireDataChanged();
        }

        // ========================================================================
        // 故障模拟方法 (Fault Injection)
        // ========================================================================

        /// <summary>注入异常温度值（模拟传感器故障，设为 999.9°C）</summary>
        public void InjectFaultyTemperature()
        {
            lock (_lock) _temperature = 999.9f;
            FireDataChanged();
        }

        /// <summary>注入异常压力值（模拟传感器故障，设为 -50.0 kPa）</summary>
        public void InjectFaultyPressure()
        {
            lock (_lock) _pressure = -50.0f;
            FireDataChanged();
        }

        /// <summary>冻结数据更新（切换到 Frozen 模式）</summary>
        public void FreezeData() { SetMode(SimulationMode.Frozen); }

        /// <summary>恢复正常数据生成（切换到 Random 模式）</summary>
        public void ResumeNormal() { SetMode(SimulationMode.Random); }

        // ========================================================================
        // 兼容旧接口 (后续步骤中 ModbusServerService / OpcUaServerService 重构后可移除)
        // ========================================================================

        /// <summary>兼容旧接口：获取 (Temp, Press, Status) 快照</summary>
        public (float Temp, float Press, bool Status) Snapshot()
        {
            lock (_lock) return (_temperature, _pressure, _running);
        }

        /// <summary>兼容旧接口：获取状态位</summary>
        public bool GetStatusCoil() { lock (_lock) return _coilRun; }
        public bool GetStatusDiscreteInput() { lock (_lock) return _diRunning; }
        public void SetStatus(bool s) { SetCoilRun(s); }

        // ========================================================================
        // 内部辅助
        // ========================================================================

        private static ushort ToUShort(double val)
        {
            if (val < 0) return 0;
            if (val > 65535) return 65535;
            return (ushort)val;
        }

        private void UpdateStats(float temp, float press)
        {
            // 温度统计
            if (temp > _tempMax) _tempMax = temp;
            if (temp < _tempMin) _tempMin = temp;
            _tempSum += temp;
            _tempSumCount++;
            _tempAvg = (float)(_tempSum / _tempSumCount);

            // 压力统计
            if (press > _pressMax) _pressMax = press;
            if (press < _pressMin) _pressMin = press;
            _pressSum += press;
            _pressSumCount++;
            _pressAvg = (float)(_pressSum / _pressSumCount);
        }

        private void UpdateAlarmStatus()
        {
            ushort status = 0;
            if ((_alarmEnableMask & 0x01) != 0 && _temperature > _alarmHighLimit) status |= 0x01;
            if ((_alarmEnableMask & 0x02) != 0 && _temperature < _alarmLowLimit) status |= 0x02;
            if ((_alarmEnableMask & 0x04) != 0 && _pressure > _alarmHighLimit * 3.33f) status |= 0x04; // 压力上限 ≈ 温度上限 * 3.33
            if ((_alarmEnableMask & 0x08) != 0 && _pressure < _alarmLowLimit * 3.33f) status |= 0x08;  // 压力下限 ≈ 温度下限 * 3.33
            _alarmStatusMask = status;
        }

        private void FireDataChanged()
        {
            DataChangedEventArgs args;
            lock (_lock)
            {
                args = new DataChangedEventArgs(
                    _temperature, _pressure, _flowRate, _level, _humidity,
                    _rpm, _voltage, _current, _power, _frequency,
                    _running, _mode, _noise, _delayMs, _samplePeriod,
                    _alarmHighLimit, _alarmLowLimit, _powerFactor,
                    _tempMax, _tempMin, _tempAvg,
                    _pressMax, _pressMin, _pressAvg,
                    _runHours, _startCount, _commCount, _errorCount,
                    _alarmEnableMask, _alarmStatusMask,
                    _faultCode1, _faultCode2, _faultCode3, _faultCode4);
            }
            DataChanged?.Invoke(this, args);
        }
    }

    /// <summary>
    /// 传感器数据快照
    /// </summary>
    public record SensorSnapshot(
        float Temperature, float Pressure, float FlowRate, float Level, float Humidity,
        int Rpm, float Voltage, float Current, float Power, float Frequency, bool Running);

    /// <summary>
    /// 数据变更事件参数
    /// </summary>
    public class DataChangedEventArgs : EventArgs
    {
        // 传感器数据
        public float Temperature { get; }
        public float Pressure { get; }
        public float FlowRate { get; }
        public float Level { get; }
        public float Humidity { get; }
        public int Rpm { get; }
        public float Voltage { get; }
        public float Current { get; }
        public float Power { get; }
        public float Frequency { get; }

        // 状态
        public bool Running { get; }

        // 控制参数
        public SharedData.SimulationMode Mode { get; }
        public float NoiseMultiplier { get; }
        public int ResponseDelayMs { get; }
        public int SamplePeriod { get; }
        public float AlarmHighLimit { get; }
        public float AlarmLowLimit { get; }
        public float PowerFactor { get; }

        // 统计
        public float TempMax { get; }
        public float TempMin { get; }
        public float TempAvg { get; }
        public float PressMax { get; }
        public float PressMin { get; }
        public float PressAvg { get; }
        public int RunHours { get; }
        public int StartCount { get; }
        public int CommCount { get; }
        public int ErrorCount { get; }

        // 报警
        public ushort AlarmEnableMask { get; }
        public ushort AlarmStatusMask { get; }
        public ushort FaultCode1 { get; }
        public ushort FaultCode2 { get; }
        public ushort FaultCode3 { get; }
        public ushort FaultCode4 { get; }

        public DataChangedEventArgs(
            float temperature, float pressure, float flowRate, float level, float humidity,
            int rpm, float voltage, float current, float power, float frequency,
            bool running, SharedData.SimulationMode mode, float noise, int delayMs, int samplePeriod,
            float alarmHighLimit, float alarmLowLimit, float powerFactor,
            float tempMax, float tempMin, float tempAvg,
            float pressMax, float pressMin, float pressAvg,
            int runHours, int startCount, int commCount, int errorCount,
            ushort alarmEnableMask, ushort alarmStatusMask,
            ushort faultCode1, ushort faultCode2, ushort faultCode3, ushort faultCode4)
        {
            Temperature = temperature;
            Pressure = pressure;
            FlowRate = flowRate;
            Level = level;
            Humidity = humidity;
            Rpm = rpm;
            Voltage = voltage;
            Current = current;
            Power = power;
            Frequency = frequency;
            Running = running;
            Mode = mode;
            NoiseMultiplier = noise;
            ResponseDelayMs = delayMs;
            SamplePeriod = samplePeriod;
            AlarmHighLimit = alarmHighLimit;
            AlarmLowLimit = alarmLowLimit;
            PowerFactor = powerFactor;
            TempMax = tempMax;
            TempMin = tempMin;
            TempAvg = tempAvg;
            PressMax = pressMax;
            PressMin = pressMin;
            PressAvg = pressAvg;
            RunHours = runHours;
            StartCount = startCount;
            CommCount = commCount;
            ErrorCount = errorCount;
            AlarmEnableMask = alarmEnableMask;
            AlarmStatusMask = alarmStatusMask;
            FaultCode1 = faultCode1;
            FaultCode2 = faultCode2;
            FaultCode3 = faultCode3;
            FaultCode4 = faultCode4;
        }
    }
}
