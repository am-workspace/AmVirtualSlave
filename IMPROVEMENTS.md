# AmVirtualSlave 改进计划

## 三阶段路线图

> **Phase 1：丰富数据** → 1个Modbus从站，足够多的数据点来测转换/背压/写入
> **Phase 2：丰富协议** → 加OPC UA完善，后续加S7/CIP等新协议
> **Phase 3：丰富从站** → Docker Compose多实例，测多站点/路由/优先级

---

## Phase 1：丰富数据模型

### 1.1 扩展寄存器地址空间

**现状**：仅6个Holding + 2个Input + 1个Discrete + 1个Coil，共约10个数据点

**目标**：扩展到模拟一个典型的工业温压流量传感器/控制器，约30+数据点

#### 新增寄存器规划

| 区域 | 地址范围 | 内容 | 测试目标 |
|------|---------|------|---------|
| **Holding 0-9** | 传感器采集值(RO) | 温度/压力/流量/液位/湿度/转速/电压/电流/功率/频率 | 数据转换（缩放/单位）、批量读取 |
| **Holding 10-19** | 设备参数(RW) | 模拟模式/噪声/延迟/采样周期/报警上限/报警下限/设备地址/波特率/数据格式/保留 | 写入控制、参数校验 |
| **Holding 20-29** | 统计数据(RO) | 温度最大/最小/平均/压力最大/最小/平均/运行小时/启动次数/通信次数/错误计数 | 统计聚合、趋势分析 |
| **Holding 100-109** | 故障/报警(RW) | 故障注入控制/报警使能/报警状态/故障码1-4/保留 | 报警路由、故障处理 |
| **Input 0-9** | 只读采集值 | 镜像Holding 0-9的传感器值（符合Modbus惯例：Input=只读采集，Holding=可写配置） | FC04只读采集测试 |
| **Discrete 0-7** | 开关量输入(RO) | 运行状态/报警状态/上限超限/下限超限/通信状态/本地/远程/保留 | FC02离散量测试 |
| **Coil 0-7** | 开关量输出(RW) | 运行控制/报警确认/复位/使能1-4/保留 | FC01/05/15写入测试 |

### 1.2 SharedData 扩展

**新增字段**：
- 传感器：`_flowRate`, `_level`, `_humidity`, `_rpm`, `_voltage`, `_current`, `_power`, `_frequency`
- 统计：`_tempMax/Min/Avg`, `_pressMax/Min/Avg`, `_runHours`, `_startCount`, `_commCount`, `_errorCount`
- 报警：`_alarmEnabled`, `_alarmStatus`, `_faultCodes[4]`
- 开关量：`_discreteInputs[8]`（8个离散输入），`_coils[8]`（8个线圈，现有Status合并进来）

**改动要点**：
- `Snapshot()` 扩展返回更多字段（或改为返回完整对象）
- `DataChangedEventArgs` 扩展属性
- `Update()` 方法适配新字段
- 新增各字段的 Get/Set 方法

### 1.3 GeneratorService 扩展

**新增数据生成逻辑**：
- 流量：20-200 m³/h，Trend模式下叠加正弦
- 液位：0-100 %，缓慢漂移
- 湿度：40-80 %，季节性周期
- 转速：1400-1600 RPM，阶跃变化
- 电压/电流/功率/频率：电力参数，互相关联（P=U*I*cosφ）
- 统计值：滚动计算最大/最小/平均
- 离散量：根据采集值自动设置报警/超限状态

### 1.4 ModbusServerService 扩展

- `DataStoreReadFrom`：处理新地址映射
- `DataStoreWrittenTo`：处理新可写寄存器
- DataStore 初始化容量需足够大（至少 200 个 Holding、20 个 Input、10 个 Discrete、10 个 Coil）

### 1.5 OpcUaServerService 扩展

- 按分组创建子文件夹：Sensors / Parameters / Statistics / Alarms / DiscreteIO
- 每个变量节点对应一个 SharedData 字段
- `NotifyDataChanged()` 的 switch 需要扩展

### 1.6 RegisterMap 扩展

- 新增所有地址常量
- `GetAllDefinitions()` 更新

### 1.7 实施拆分

为保证正确率，拆分为以下子步骤：

1. **1.7.1 RegisterMap + SharedData 字段**：定义地址常量，添加新字段和 Get/Set，更新 DataChangedEventArgs
2. **1.7.2 GeneratorService**：扩展数据生成逻辑
3. **1.7.3 ModbusServerService**：扩展 DataStore 读写事件
4. **1.7.4 OpcUaServerService**：扩展节点和通知
5. **1.7.5 编译验证 + 端到端测试**

---

## Phase 2：丰富协议 — OPC UA 协议层完善

> 安全策略/用户认证/历史数据：测试从站不需要，已去掉（Modbus也没有这些）
> 聚焦 OPC UA 独有特性：方法调用 + 节点属性

### 2.1 方法调用（Method Call）

OPC UA 独有特性，Modbus 没有方法调用概念，网关需要测试此差异。

| 方法 | 参数 | 效果 | 对应 Modbus 行为 |
|------|------|------|-----------------|
| Start() | 无 | Running=true | 写 CoilRun=1 |
| Stop() | 无 | Running=false | 写 CoilRun=0 |
| Reset() | 无 | 清零统计+故障码 | 写多个寄存器 |
| AcknowledgeAlarm() | 无 | 清除报警状态 | 写 AlarmStatusMask=0 |
| SetFaultCode(index, code) | UInt16, UInt16 | 注入指定故障码 | 写 Holding 103-106 |

### 2.2 节点属性（Property） ✅

为变量节点添加工程属性，供网关测试数据质量/元数据读取：

| 属性 | 内容 | 例子 |
|------|------|------|
| EngineeringUnits | 单位 | °C, kPa, m³/h, V, A, W, Hz, %RH, RPM |
| EURange | 量程范围 | Temperature: -40~150°C |
| Description | 详细描述 | 已有，补充中文 |

### 2.3 后续：S7 Communication、CIP/EtherNet/IP 协议支持

---

## Phase 3：丰富从站 — 多实例运行

> 不依赖 Docker，通过配置文件 + 环境变量实现多实例

### 3.1 配置覆盖机制 ✅

- `Program.cs` 支持 `--config <path>` 加载额外配置文件（覆盖 appsettings.json）
- 支持 `AMVS_` 前缀环境变量覆盖（例：`AMVS_Modbus__Port=5021`）
- 优先级：环境变量 > 额外配置文件 > appsettings.json

### 3.2 多从站配置文件 ✅

| 实例 | 配置文件 | Modbus | OPC UA | MQTT Topic | 模式 |
|------|---------|--------|--------|-----------|------|
| SlaveA | `appsettings.SlaveA.json` | :5020, Id=1 | :4840 | industrial/slaveA | Random |
| SlaveB | `appsettings.SlaveB.json` | :5021, Id=2 | :4841 | industrial/slaveB | Sine |

### 3.3 启动/停止脚本 ✅

- `start-slaves.bat` — 双窗口启动 SlaveA + SlaveB
- `stop-slaves.bat` — 统一关闭所有从站实例
- `start-slave-env.bat` — 环境变量方式启动示例

### 3.4 协议开关 ✅

每个协议都有 `Enabled` 字段，可按需启用/禁用，模拟真实设备（只支持一种协议）：

| 配置 | 模拟设备 |
|------|---------|
| Modbus=true, OpcUa=false, Mqtt=false | 纯 Modbus 传感器/PLC |
| Modbus=false, OpcUa=true, Mqtt=false | 纯 OPC UA 设备 |
| Modbus=true, OpcUa=true, Mqtt=true | 协议网关（默认） |

### 3.4 后续：Docker 化

- Dockerfile + docker-compose.yml（等装了 Docker 再做）
- K8s 部署清单（可选）

---

## 已完成项目

### ✅ Modbus 补全 Input Register 和 Discrete Input
### ✅ Modbus RTU 串口支持
### ✅ OPC UA 写支持
### ✅ MQTT 断线重连（基础版）
### ✅ 多从站讨论 → 改为 Docker 多实例方案
### ✅ Phase 1：丰富数据模型（37个数据点，4类寄存器）
### ✅ Phase 2：OPC UA 方法调用 + EngineeringUnits/EURange 属性
### ✅ Phase 3：多实例运行（配置文件覆盖 + 环境变量 + 启动脚本）
