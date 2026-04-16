using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AmVritualSlave.Core
{
    /// <summary>
    /// 寄存器映射元数据：用于描述每个寄存器的详细信息，方便生成文档。
    /// </summary>
    public class RegisterDefinition
    {
        public ushort Address { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = ""; // "HoldingRegister", "InputRegister", "Coil", "DiscreteInput"
        public bool IsWritable { get; set; } = false;
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "";
        public string ScaleInfo { get; set; } = ""; // 例如："Value / 10"
    }

    /// <summary>
    /// 寄存器地址常量定义中心。
    /// 所有 Modbus 地址必须在此定义，禁止在业务逻辑中直接使用数字字面量。
    /// </summary>
    public static class RegisterMap
    {
        // ========================================================================
        // Holding Registers — 传感器采集值 (功能码 03, 只读)
        // 地址 0-9
        // ========================================================================

        /// <summary>地址 0: 温度 (放大10倍, °C)</summary>
        public const ushort Temperature = 0;

        /// <summary>地址 1: 压力 (放大10倍, kPa)</summary>
        public const ushort Pressure = 1;

        /// <summary>地址 2: 流量 (放大10倍, m³/h)</summary>
        public const ushort FlowRate = 2;

        /// <summary>地址 3: 液位 (放大10倍, %)</summary>
        public const ushort Level = 3;

        /// <summary>地址 4: 湿度 (放大10倍, %RH)</summary>
        public const ushort Humidity = 4;

        /// <summary>地址 5: 转速 (直写, RPM)</summary>
        public const ushort Rpm = 5;

        /// <summary>地址 6: 电压 (放大10倍, V)</summary>
        public const ushort Voltage = 6;

        /// <summary>地址 7: 电流 (放大100倍, A)</summary>
        public const ushort Current = 7;

        /// <summary>地址 8: 功率 (放大10倍, W)</summary>
        public const ushort Power = 8;

        /// <summary>地址 9: 频率 (放大100倍, Hz)</summary>
        public const ushort Frequency = 9;

        // ========================================================================
        // Holding Registers — 设备参数 (功能码 03/06/16, 可写)
        // 地址 10-19
        // ========================================================================

        /// <summary>地址 10: 模拟模式 (0=Random, 1=Trend, 2=Frozen)</summary>
        public const ushort SimulationMode = 10;

        /// <summary>地址 11: 噪声系数 (放大100倍)</summary>
        public const ushort NoiseMultiplier = 11;

        /// <summary>地址 12: 响应延迟 (ms)</summary>
        public const ushort ResponseDelayMs = 12;

        /// <summary>地址 13: 采样周期 (ms, 100-60000)</summary>
        public const ushort SamplePeriod = 13;

        /// <summary>地址 14: 报警上限 (放大10倍, 与温度对应)</summary>
        public const ushort AlarmHighLimit = 14;

        /// <summary>地址 15: 报警下限 (放大10倍, 与温度对应)</summary>
        public const ushort AlarmLowLimit = 15;

        /// <summary>地址 16: 设备地址 (1-247)</summary>
        public const ushort DeviceAddress = 16;

        /// <summary>地址 17: 通信波特率 (枚举: 0=9600, 1=19200, 2=38400, 3=115200)</summary>
        public const ushort BaudRateCode = 17;

        /// <summary>地址 18: 功率因数 (放大100倍, cosφ)</summary>
        public const ushort PowerFactor = 18;

        /// <summary>地址 19: 保留</summary>
        public const ushort ReservedParam = 19;

        // ========================================================================
        // Holding Registers — 统计数据 (功能码 03, 只读)
        // 地址 20-29
        // ========================================================================

        /// <summary>地址 20: 温度最大值 (放大10倍, °C)</summary>
        public const ushort TempMax = 20;

        /// <summary>地址 21: 温度最小值 (放大10倍, °C)</summary>
        public const ushort TempMin = 21;

        /// <summary>地址 22: 温度平均值 (放大10倍, °C)</summary>
        public const ushort TempAvg = 22;

        /// <summary>地址 23: 压力最大值 (放大10倍, kPa)</summary>
        public const ushort PressMax = 23;

        /// <summary>地址 24: 压力最小值 (放大10倍, kPa)</summary>
        public const ushort PressMin = 24;

        /// <summary>地址 25: 压力平均值 (放大10倍, kPa)</summary>
        public const ushort PressAvg = 25;

        /// <summary>地址 26: 累计运行小时 (h)</summary>
        public const ushort RunHours = 26;

        /// <summary>地址 27: 启动次数</summary>
        public const ushort StartCount = 27;

        /// <summary>地址 28: 通信次数</summary>
        public const ushort CommCount = 28;

        /// <summary>地址 29: 错误计数</summary>
        public const ushort ErrorCount = 29;

        // ========================================================================
        // Holding Registers — 故障/报警 (功能码 03/06/16)
        // 地址 100-109
        // ========================================================================

        /// <summary>地址 100: 故障注入控制 (0=恢复正常, 1=异常温度, 2=异常压力, 3=冻结数据)</summary>
        public const ushort FaultInjectionControl = 100;

        /// <summary>地址 101: 报警使能位掩码 (bit0=温度上限, bit1=温度下限, bit2=压力上限, bit3=压力下限)</summary>
        public const ushort AlarmEnableMask = 101;

        /// <summary>地址 102: 报警状态位掩码 (只读, bit0=温度超上限, bit1=温度超下限, bit2=压力超上限, bit3=压力超下限)</summary>
        public const ushort AlarmStatusMask = 102;

        /// <summary>地址 103: 故障码1</summary>
        public const ushort FaultCode1 = 103;

        /// <summary>地址 104: 故障码2</summary>
        public const ushort FaultCode2 = 104;

        /// <summary>地址 105: 故障码3</summary>
        public const ushort FaultCode3 = 105;

        /// <summary>地址 106: 故障码4</summary>
        public const ushort FaultCode4 = 106;

        // ========================================================================
        // Input Registers — 只读采集值 (功能码 04)
        // 地址 0-9 (镜像 Holding 0-9 的传感器值)
        // ========================================================================

        /// <summary>地址 0: 温度采集值 (放大10倍)</summary>
        public const ushort InputTemperature = 0;

        /// <summary>地址 1: 压力采集值 (放大10倍)</summary>
        public const ushort InputPressure = 1;

        /// <summary>地址 2: 流量采集值 (放大10倍)</summary>
        public const ushort InputFlowRate = 2;

        /// <summary>地址 3: 液位采集值 (放大10倍)</summary>
        public const ushort InputLevel = 3;

        /// <summary>地址 4: 湿度采集值 (放大10倍)</summary>
        public const ushort InputHumidity = 4;

        /// <summary>地址 5: 转速采集值</summary>
        public const ushort InputRpm = 5;

        /// <summary>地址 6: 电压采集值 (放大10倍)</summary>
        public const ushort InputVoltage = 6;

        /// <summary>地址 7: 电流采集值 (放大100倍)</summary>
        public const ushort InputCurrent = 7;

        /// <summary>地址 8: 功率采集值 (放大10倍)</summary>
        public const ushort InputPower = 8;

        /// <summary>地址 9: 频率采集值 (放大100倍)</summary>
        public const ushort InputFrequency = 9;

        // ========================================================================
        // Discrete Inputs — 只读开关量 (功能码 02)
        // 地址 0-7
        // ========================================================================

        /// <summary>地址 0: 设备运行状态 (1=运行, 0=停止)</summary>
        public const ushort DiRunning = 0;

        /// <summary>地址 1: 报警状态 (1=有报警, 0=无报警)</summary>
        public const ushort DiAlarm = 1;

        /// <summary>地址 2: 温度超上限 (1=超限, 0=正常)</summary>
        public const ushort DiTempHigh = 2;

        /// <summary>地址 3: 温度超下限 (1=超限, 0=正常)</summary>
        public const ushort DiTempLow = 3;

        /// <summary>地址 4: 通信正常 (1=正常, 0=故障)</summary>
        public const ushort DiCommOk = 4;

        /// <summary>地址 5: 本地模式 (1=本地, 0=远程)</summary>
        public const ushort DiLocalMode = 5;

        /// <summary>地址 6: 就绪 (1=就绪, 0=未就绪)</summary>
        public const ushort DiReady = 6;

        /// <summary>地址 7: 保留</summary>
        public const ushort DiReserved = 7;

        // ========================================================================
        // Coils — 可写开关量 (功能码 01/05/15)
        // 地址 0-7
        // ========================================================================

        /// <summary>地址 0: 设备运行控制 (1=启动, 0=停止)</summary>
        public const ushort CoilRun = 0;

        /// <summary>地址 1: 报警确认 (1=确认, 自动复位)</summary>
        public const ushort CoilAlarmAck = 1;

        /// <summary>地址 2: 故障复位 (1=复位)</summary>
        public const ushort CoilFaultReset = 2;

        /// <summary>地址 3: 使能1 (通用使能位)</summary>
        public const ushort CoilEnable1 = 3;

        /// <summary>地址 4: 使能2 (通用使能位)</summary>
        public const ushort CoilEnable2 = 4;

        /// <summary>地址 5: 使能3 (通用使能位)</summary>
        public const ushort CoilEnable3 = 5;

        /// <summary>地址 6: 使能4 (通用使能位)</summary>
        public const ushort CoilEnable4 = 6;

        /// <summary>地址 7: 保留</summary>
        public const ushort CoilReserved = 7;

        // ========================================================================
        // 文档生成辅助方法
        // ========================================================================

        /// <summary>
        /// 获取所有寄存器的定义列表，用于生成文档或验证。
        /// </summary>
        public static List<RegisterDefinition> GetAllDefinitions()
        {
            return new List<RegisterDefinition>
            {
                // --- Holding Registers: 传感器采集值 (只读) ---
                new RegisterDefinition { Address = Temperature, Name = "Temperature", Type = "HoldingRegister", IsWritable = false, Description = "当前环境温度", Unit = "°C", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = Pressure, Name = "Pressure", Type = "HoldingRegister", IsWritable = false, Description = "当前系统压力", Unit = "kPa", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = FlowRate, Name = "FlowRate", Type = "HoldingRegister", IsWritable = false, Description = "管道流量", Unit = "m³/h", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = Level, Name = "Level", Type = "HoldingRegister", IsWritable = false, Description = "液位高度", Unit = "%", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = Humidity, Name = "Humidity", Type = "HoldingRegister", IsWritable = false, Description = "环境湿度", Unit = "%RH", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = Rpm, Name = "Rpm", Type = "HoldingRegister", IsWritable = false, Description = "电机转速", Unit = "RPM", ScaleInfo = "直接读取" },
                new RegisterDefinition { Address = Voltage, Name = "Voltage", Type = "HoldingRegister", IsWritable = false, Description = "电压", Unit = "V", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = Current, Name = "Current", Type = "HoldingRegister", IsWritable = false, Description = "电流", Unit = "A", ScaleInfo = "寄存器值 / 100.0" },
                new RegisterDefinition { Address = Power, Name = "Power", Type = "HoldingRegister", IsWritable = false, Description = "有功功率", Unit = "W", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = Frequency, Name = "Frequency", Type = "HoldingRegister", IsWritable = false, Description = "电网频率", Unit = "Hz", ScaleInfo = "寄存器值 / 100.0" },

                // --- Holding Registers: 设备参数 (可写) ---
                new RegisterDefinition { Address = SimulationMode, Name = "SimulationMode", Type = "HoldingRegister", IsWritable = true, Description = "模拟器运行模式 (0:Random, 1:Trend, 2:Frozen)", Unit = "Enum", ScaleInfo = "直接写入枚举整数值" },
                new RegisterDefinition { Address = NoiseMultiplier, Name = "NoiseMultiplier", Type = "HoldingRegister", IsWritable = true, Description = "数据波动噪声系数", Unit = "Factor", ScaleInfo = "寄存器值 / 100.0" },
                new RegisterDefinition { Address = ResponseDelayMs, Name = "ResponseDelayMs", Type = "HoldingRegister", IsWritable = true, Description = "模拟响应延迟时间", Unit = "ms", ScaleInfo = "直接写入毫秒数" },
                new RegisterDefinition { Address = SamplePeriod, Name = "SamplePeriod", Type = "HoldingRegister", IsWritable = true, Description = "采样周期", Unit = "ms", ScaleInfo = "100-60000ms" },
                new RegisterDefinition { Address = AlarmHighLimit, Name = "AlarmHighLimit", Type = "HoldingRegister", IsWritable = true, Description = "报警上限", Unit = "°C", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = AlarmLowLimit, Name = "AlarmLowLimit", Type = "HoldingRegister", IsWritable = true, Description = "报警下限", Unit = "°C", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = DeviceAddress, Name = "DeviceAddress", Type = "HoldingRegister", IsWritable = true, Description = "设备地址", Unit = "", ScaleInfo = "1-247" },
                new RegisterDefinition { Address = BaudRateCode, Name = "BaudRateCode", Type = "HoldingRegister", IsWritable = true, Description = "通信波特率 (0=9600, 1=19200, 2=38400, 3=115200)", Unit = "Enum", ScaleInfo = "直接写入枚举值" },
                new RegisterDefinition { Address = PowerFactor, Name = "PowerFactor", Type = "HoldingRegister", IsWritable = true, Description = "功率因数 cosφ", Unit = "", ScaleInfo = "寄存器值 / 100.0 (0.00-1.00)" },

                // --- Holding Registers: 统计数据 (只读) ---
                new RegisterDefinition { Address = TempMax, Name = "TempMax", Type = "HoldingRegister", IsWritable = false, Description = "温度最大值", Unit = "°C", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = TempMin, Name = "TempMin", Type = "HoldingRegister", IsWritable = false, Description = "温度最小值", Unit = "°C", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = TempAvg, Name = "TempAvg", Type = "HoldingRegister", IsWritable = false, Description = "温度平均值", Unit = "°C", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = PressMax, Name = "PressMax", Type = "HoldingRegister", IsWritable = false, Description = "压力最大值", Unit = "kPa", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = PressMin, Name = "PressMin", Type = "HoldingRegister", IsWritable = false, Description = "压力最小值", Unit = "kPa", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = PressAvg, Name = "PressAvg", Type = "HoldingRegister", IsWritable = false, Description = "压力平均值", Unit = "kPa", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = RunHours, Name = "RunHours", Type = "HoldingRegister", IsWritable = false, Description = "累计运行小时数", Unit = "h", ScaleInfo = "直接读取" },
                new RegisterDefinition { Address = StartCount, Name = "StartCount", Type = "HoldingRegister", IsWritable = false, Description = "启动次数", Unit = "", ScaleInfo = "直接读取" },
                new RegisterDefinition { Address = CommCount, Name = "CommCount", Type = "HoldingRegister", IsWritable = false, Description = "通信次数", Unit = "", ScaleInfo = "直接读取" },
                new RegisterDefinition { Address = ErrorCount, Name = "ErrorCount", Type = "HoldingRegister", IsWritable = false, Description = "错误计数", Unit = "", ScaleInfo = "直接读取" },

                // --- Holding Registers: 故障/报警 ---
                new RegisterDefinition { Address = FaultInjectionControl, Name = "FaultInjectionControl", Type = "HoldingRegister", IsWritable = true, Description = "故障注入控制 (0:正常, 1:异常温度, 2:异常压力, 3:冻结数据)", Unit = "Enum", ScaleInfo = "直接写入控制码" },
                new RegisterDefinition { Address = AlarmEnableMask, Name = "AlarmEnableMask", Type = "HoldingRegister", IsWritable = true, Description = "报警使能位掩码 (bit0=温度上限, bit1=温度下限, bit2=压力上限, bit3=压力下限)", Unit = "BitMask", ScaleInfo = "直接写入位掩码" },
                new RegisterDefinition { Address = AlarmStatusMask, Name = "AlarmStatusMask", Type = "HoldingRegister", IsWritable = false, Description = "报警状态位掩码 (bit0=温度超上限, bit1=温度超下限, bit2=压力超上限, bit3=压力超下限)", Unit = "BitMask", ScaleInfo = "只读" },
                new RegisterDefinition { Address = FaultCode1, Name = "FaultCode1", Type = "HoldingRegister", IsWritable = true, Description = "故障码1", Unit = "", ScaleInfo = "直接写入" },
                new RegisterDefinition { Address = FaultCode2, Name = "FaultCode2", Type = "HoldingRegister", IsWritable = true, Description = "故障码2", Unit = "", ScaleInfo = "直接写入" },
                new RegisterDefinition { Address = FaultCode3, Name = "FaultCode3", Type = "HoldingRegister", IsWritable = true, Description = "故障码3", Unit = "", ScaleInfo = "直接写入" },
                new RegisterDefinition { Address = FaultCode4, Name = "FaultCode4", Type = "HoldingRegister", IsWritable = true, Description = "故障码4", Unit = "", ScaleInfo = "直接写入" },

                // --- Input Registers: 只读采集值 (镜像 Holding 0-9) ---
                new RegisterDefinition { Address = InputTemperature, Name = "InputTemperature", Type = "InputRegister", IsWritable = false, Description = "温度采集值", Unit = "°C", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = InputPressure, Name = "InputPressure", Type = "InputRegister", IsWritable = false, Description = "压力采集值", Unit = "kPa", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = InputFlowRate, Name = "InputFlowRate", Type = "InputRegister", IsWritable = false, Description = "流量采集值", Unit = "m³/h", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = InputLevel, Name = "InputLevel", Type = "InputRegister", IsWritable = false, Description = "液位采集值", Unit = "%", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = InputHumidity, Name = "InputHumidity", Type = "InputRegister", IsWritable = false, Description = "湿度采集值", Unit = "%RH", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = InputRpm, Name = "InputRpm", Type = "InputRegister", IsWritable = false, Description = "转速采集值", Unit = "RPM", ScaleInfo = "直接读取" },
                new RegisterDefinition { Address = InputVoltage, Name = "InputVoltage", Type = "InputRegister", IsWritable = false, Description = "电压采集值", Unit = "V", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = InputCurrent, Name = "InputCurrent", Type = "InputRegister", IsWritable = false, Description = "电流采集值", Unit = "A", ScaleInfo = "寄存器值 / 100.0" },
                new RegisterDefinition { Address = InputPower, Name = "InputPower", Type = "InputRegister", IsWritable = false, Description = "功率采集值", Unit = "W", ScaleInfo = "寄存器值 / 10.0" },
                new RegisterDefinition { Address = InputFrequency, Name = "InputFrequency", Type = "InputRegister", IsWritable = false, Description = "频率采集值", Unit = "Hz", ScaleInfo = "寄存器值 / 100.0" },

                // --- Discrete Inputs (只读状态输入) ---
                new RegisterDefinition { Address = DiRunning, Name = "DiRunning", Type = "DiscreteInput", IsWritable = false, Description = "设备运行状态", Unit = "Boolean", ScaleInfo = "0=停止, 1=运行" },
                new RegisterDefinition { Address = DiAlarm, Name = "DiAlarm", Type = "DiscreteInput", IsWritable = false, Description = "报警状态", Unit = "Boolean", ScaleInfo = "0=无报警, 1=有报警" },
                new RegisterDefinition { Address = DiTempHigh, Name = "DiTempHigh", Type = "DiscreteInput", IsWritable = false, Description = "温度超上限", Unit = "Boolean", ScaleInfo = "0=正常, 1=超限" },
                new RegisterDefinition { Address = DiTempLow, Name = "DiTempLow", Type = "DiscreteInput", IsWritable = false, Description = "温度超下限", Unit = "Boolean", ScaleInfo = "0=正常, 1=超限" },
                new RegisterDefinition { Address = DiCommOk, Name = "DiCommOk", Type = "DiscreteInput", IsWritable = false, Description = "通信正常", Unit = "Boolean", ScaleInfo = "0=故障, 1=正常" },
                new RegisterDefinition { Address = DiLocalMode, Name = "DiLocalMode", Type = "DiscreteInput", IsWritable = false, Description = "本地模式", Unit = "Boolean", ScaleInfo = "0=远程, 1=本地" },
                new RegisterDefinition { Address = DiReady, Name = "DiReady", Type = "DiscreteInput", IsWritable = false, Description = "就绪状态", Unit = "Boolean", ScaleInfo = "0=未就绪, 1=就绪" },

                // --- Coils (可写开关) ---
                new RegisterDefinition { Address = CoilRun, Name = "CoilRun", Type = "Coil", IsWritable = true, Description = "设备运行控制", Unit = "Boolean", ScaleInfo = "0=停止, 1=启动" },
                new RegisterDefinition { Address = CoilAlarmAck, Name = "CoilAlarmAck", Type = "Coil", IsWritable = true, Description = "报警确认", Unit = "Boolean", ScaleInfo = "1=确认(自动复位)" },
                new RegisterDefinition { Address = CoilFaultReset, Name = "CoilFaultReset", Type = "Coil", IsWritable = true, Description = "故障复位", Unit = "Boolean", ScaleInfo = "1=复位" },
                new RegisterDefinition { Address = CoilEnable1, Name = "CoilEnable1", Type = "Coil", IsWritable = true, Description = "使能位1", Unit = "Boolean", ScaleInfo = "0=禁用, 1=启用" },
                new RegisterDefinition { Address = CoilEnable2, Name = "CoilEnable2", Type = "Coil", IsWritable = true, Description = "使能位2", Unit = "Boolean", ScaleInfo = "0=禁用, 1=启用" },
                new RegisterDefinition { Address = CoilEnable3, Name = "CoilEnable3", Type = "Coil", IsWritable = true, Description = "使能位3", Unit = "Boolean", ScaleInfo = "0=禁用, 1=启用" },
                new RegisterDefinition { Address = CoilEnable4, Name = "CoilEnable4", Type = "Coil", IsWritable = true, Description = "使能位4", Unit = "Boolean", ScaleInfo = "0=禁用, 1=启用" },
            };
        }

        /// <summary>
        /// 生成 Markdown 格式的寄存器映射表。
        /// </summary>
        public static string GenerateMarkdownTable()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Modbus Register Map");
            sb.AppendLine();
            sb.AppendLine("自动生成的寄存器映射文档。请勿手动修改此文件，请更新 `RegisterMap.cs` 后重新生成。");
            sb.AppendLine();
            sb.AppendLine("| 地址 (Addr) | 名称 (Name) | 类型 (Type) | 读写 (R/W) | 单位 (Unit) | 缩放/说明 (Scale/Note) | 描述 (Description) |");
            sb.AppendLine("| :--- | :--- | :--- | :---: | :---: | :--- | :--- |");

            var regs = GetAllDefinitions().OrderBy(r => r.Type).ThenBy(r => r.Address);

            foreach (var reg in regs)
            {
                string rw = reg.IsWritable ? "RW" : "RO";
                sb.AppendLine($"| {reg.Address} | `{reg.Name}` | {reg.Type} | {rw} | {reg.Unit} | {reg.ScaleInfo} | {reg.Description} |");
            }

            sb.AppendLine();
            sb.AppendLine("## 枚举值参考");
            sb.AppendLine("### SimulationMode (地址 10)");
            sb.AppendLine("- `0`: Random (随机波动)");
            sb.AppendLine("- `1`: Trend (正弦波趋势)");
            sb.AppendLine("- `2`: Frozen (数值冻结)");
            sb.AppendLine();
            sb.AppendLine("### BaudRateCode (地址 17)");
            sb.AppendLine("- `0`: 9600");
            sb.AppendLine("- `1`: 19200");
            sb.AppendLine("- `2`: 38400");
            sb.AppendLine("- `3`: 115200");
            sb.AppendLine();
            sb.AppendLine("### FaultInjectionControl (地址 100)");
            sb.AppendLine("- `0`: ResumeNormal (恢复正常)");
            sb.AppendLine("- `1`: FaultyTemperature (异常温度 999.9°C)");
            sb.AppendLine("- `2`: FaultyPressure (异常压力 -50.0 kPa)");
            sb.AppendLine("- `3`: FreezeData (冻结数据更新)");
            sb.AppendLine();
            sb.AppendLine("### AlarmEnableMask (地址 101)");
            sb.AppendLine("- `bit0`: 温度上限报警使能");
            sb.AppendLine("- `bit1`: 温度下限报警使能");
            sb.AppendLine("- `bit2`: 压力上限报警使能");
            sb.AppendLine("- `bit3`: 压力下限报警使能");

            return sb.ToString();
        }
    }
}
