# AmVritualSlave

工业虚拟从站模拟器，为网关（AmGateway）提供完整的测试环境。模拟工业温压流量控制器，同时暴露 Modbus TCP/RTU、OPC UA、MQTT 三种协议，支持多实例并行运行。

## 架构

```
┌─────────────────────────────────────────────────────┐
│                  AmVritualSlave                     │
│                                                     │
│  ┌─────────────┐    ┌──────────────────────────┐   │
│  │ Generator   │───▶│       SharedData          │   │
│  │ Service     │    │  (线程安全的数据中心)       │   │
│  │ 数据生成器   │    │  10传感器 + 9参数 + 10统计 │   │
│  │             │    │  + 7报警 + 7离散输入 + 7线圈 │   │
│  └─────────────┘    └──────┬──────────────────────┘   │
│                            │                          │
│              ┌─────────────┼─────────────┐            │
│              ▼             ▼             ▼            │
│  ┌──────────────┐ ┌──────────────┐ ┌───────────┐    │
│  │ Modbus TCP   │ │   OPC UA     │ │   MQTT    │    │
│  │ /RTU Server  │ │   Server     │ │ Publisher │    │
│  │ :5020        │ │   :4840      │ │  (可选)   │    │
│  │ SlaveId=1    │ │   Anonymous  │ │           │    │
│  └──────────────┘ └──────────────┘ └───────────┘    │
│                                                     │
└─────────────────────────────────────────────────────┘
                         │
                         ▼
              ┌─────────────────────┐
              │    AmGateway 网关    │
              │  (Modbus + OPC UA   │
              │    采集 + MQTT 转发)  │
              └─────────────────────┘
```

核心设计：**GeneratorService 生成数据 → SharedData 集中存储 → 三个协议服务各自读取发布**，数据源统一，协议表现一致。

## 数据模型

模拟一个完整的工业温压流量控制器，37个数据点：

### Modbus 寄存器映射

| 区域 | 地址 | 内容 | 读写 | 功能码 |
|------|------|------|------|--------|
| Holding 0-9 | 传感器采集值 | 温度/压力/流量/液位/湿度/转速/电压/电流/功率/频率 | RO | FC03 |
| Holding 10-19 | 设备参数 | 模式/噪声/延迟/采样周期/报警上限/下限/设备地址/波特率/功率因数/保留 | RW | FC03/06/16 |
| Holding 20-29 | 统计数据 | 温度最大/最小/平均/压力统计/运行小时/启动次数/通信次数/错误计数 | RO | FC03 |
| Holding 100-106 | 故障/报警 | 故障注入/报警使能/报警状态/故障码1-4 | RW | FC03/06 |
| Input 0-9 | 只读采集值 | 镜像 Holding 0-9 | RO | FC04 |
| Discrete 0-6 | 开关量输入 | 运行/报警/超限/通信/本地/远程 | RO | FC02 |
| Coil 0-6 | 开关量输出 | 运行控制/报警确认/复位/使能1-4 | RW | FC01/05/15 |

### OPC UA 地址空间

```
Industrial/
├── Sensors/      10变量 + EngineeringUnits + EURange
├── Parameters/   8变量 (RW)
├── Statistics/   10变量 + EngineeringUnits + EURange
├── Alarms/       12变量 (故障码 + 离散量)
└── Methods/      5方法
    ├── Start()                → 启动设备
    ├── Stop()                 → 停止设备
    ├── Reset()                → 重置统计+故障码
    ├── AcknowledgeAlarm()     → 确认报警
    └── SetFaultCode(i, code)  → 注入故障码
```

### 数据关联

电力参数物理关联：**P = U × I × cosφ**，用于测试网关的数据转换和关联计算能力。

## 协议支持

| 协议 | 功能 | 默认端口 |
|------|------|---------|
| **Modbus TCP** | FC01/02/03/04/05/06/15/16 全功能码 | 5020 |
| **Modbus RTU** | 串口通信（COM1, 9600/N/8/1） | - |
| **OPC UA** | 读写 + 订阅 + 方法调用 + EngineeringUnits/EURange | 4840 |
| **MQTT** | 数据发布（默认关闭） | 1883 |

每个协议可独立启用/禁用，模拟真实设备（通常只支持一种协议）。

## 数据生成模式

| 模式 | 行为 |
|------|------|
| Random | 随机波动（默认） |
| Sine | 正弦周期变化 |
| Step | 阶跃变化 |
| Trend | 线性趋势 + 噪声 |
| Frozen | 冻结当前值不变 |

## 快速开始

### 前置条件

- .NET 10 SDK

### 单实例运行

```bash
cd AmVritualSlave
dotnet run --project AmVritualSlave
```

默认启动 Modbus(:5020) + OPC UA(:4840)，MQTT 关闭。

### 多实例运行

```bash
# 方式1：启动脚本（开两个窗口）
start-slaves.bat

# 方式2：指定配置文件
dotnet run --project AmVritualSlave -- --config appsettings.SlaveA.json
dotnet run --project AmVritualSlave -- --config appsettings.SlaveB.json

# 方式3：环境变量覆盖
set AMVS_Modbus__Port=5021
set AMVS_Modbus__SlaveId=2
set AMVS_OpcUa__Port=4841
dotnet run --project AmVritualSlave

# 停止所有实例
stop-slaves.bat
```

### 多实例配置

| 实例 | 配置文件 | Modbus | OPC UA | MQTT Topic | 模式 |
|------|---------|--------|--------|-----------|------|
| SlaveA | `appsettings.SlaveA.json` | :5020, Id=1 | :4840 | industrial/slaveA | Random |
| SlaveB | `appsettings.SlaveB.json` | :5021, Id=2 | :4841 | industrial/slaveB | Sine |

## 配置说明

`appsettings.json` 关键配置项：

```json
{
  "Modbus": {
    "Enabled": true,
    "Mode": "Tcp",          // Tcp 或 Rtu
    "Port": 5020,
    "SlaveId": 1,
    "IpAddress": "0.0.0.0"
  },
  "OpcUa": {
    "Enabled": true,
    "Port": 4840,
    "ApplicationName": "AmVritualSlave",
    "ApplicationUri": "urn:localhost:AmVritualSlave"
  },
  "Mqtt": {
    "Enabled": false,       // 默认关闭，设备通常不支持MQTT
    "Broker": "localhost",
    "Port": 1883,
    "TopicPrefix": "industrial"
  },
  "Simulation": {
    "InitialMode": "Random",
    "UpdateIntervalMs": 2000,
    "DefaultNoise": 1.0,
    "DefaultDelayMs": 0
  }
}
```

配置优先级：`AMVS_ 环境变量` > `--config 指定文件` > `appsettings.json`

支持热重载：修改 `appsettings.json` 后，端口/模式等关键参数变更会自动重启对应服务。

## 项目结构

```
AmVritualSlave/
├── AmVritualSlave/              # 主程序入口
│   ├── Program.cs               # 启动配置、DI、环境变量支持
│   ├── appsettings.json         # 默认配置
│   ├── appsettings.SlaveA.json  # 从站A配置
│   └── appsettings.SlaveB.json  # 从站B配置
├── AmVritualSlave.Core/         # 核心库
│   ├── SharedData.cs            # 线程安全数据中心
│   ├── RegisterMap.cs           # 寄存器地址定义
│   ├── GeneratorService.cs      # 数据生成器
│   ├── ModbusServerService.cs   # Modbus TCP/RTU 服务
│   ├── OpcUaServerService.cs    # OPC UA 服务
│   ├── MqttPublisherService.cs  # MQTT 发布服务
│   └── AppSettings.cs           # 配置映射类
├── start-slaves.bat             # 启动双从站
├── stop-slaves.bat              # 停止所有从站
├── start-slave-env.bat          # 环境变量启动示例
├── IMPROVEMENTS.md              # 改进计划
└── README.md                    # 本文件
```

## 测试工具推荐

| 工具 | 用途 |
|------|------|
| [Modbus Poll](https://www.modbustools.com/) | Modbus TCP/RTU 客户端测试 |
| [UA Expert](https://www.unified-automation.com/) | OPC UA 客户端浏览/订阅/方法调用 |
| [MQTT Explorer](https://mqtt-explorer.com/) | MQTT 消息查看 |
| [QModMaster](https://sourceforge.net/projects/qmodmaster/) | 开源 Modbus 客户端 |

## 技术栈

- .NET 10
- NModbus4.NetCore (Modbus)
- OPC Foundation UA .NET Standard Library (OPC UA)
- MQTTnet (MQTT)
- Serilog (日志)
