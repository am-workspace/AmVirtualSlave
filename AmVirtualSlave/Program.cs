using AmVirtualSlave.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AmVirtualSlave
{
    /// <summary>
    /// 虚拟从站模拟器入口：配置依赖注入并启动后台服务。
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(Directory.GetCurrentDirectory());
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                    // 支持 --config <path> 指定额外配置文件（后加载覆盖前面的）
                    var configArg = GetConfigArg(args);
                    if (configArg != null)
                    {
                        config.AddJsonFile(configArg, optional: true, reloadOnChange: true);
                    }

                    // 环境变量覆盖，前缀 "AMVS_"，双下划线分隔层级
                    // 例: AMVS_Modbus__Port=5021, AMVS_OpcUa__Enabled=true
                    config.AddEnvironmentVariables("AMVS_");
                })
                .ConfigureServices((context, services) =>
                {
                    services.Configure<AppSettings>(context.Configuration);
                    services.AddSingleton<SharedData>();
                    services.AddHostedService<GeneratorService>();
                    services.AddHostedService<ModbusServerService>();
                    services.AddHostedService<OpcUaServerService>();
                    services.AddHostedService<MqttPublisherService>();
                })
                .UseSerilog((context, services, configuration) =>
                {
                    configuration.ReadFrom.Configuration(context.Configuration);
                })
                .Build();

            await RunInitializationAsync(host.Services);
            await host.RunAsync();
        }

        private static readonly ILogger _log = Log.ForContext<Program>();

        /// <summary>
        /// 从命令行参数中提取 --config 的值
        /// </summary>
        private static string? GetConfigArg(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--config", StringComparison.OrdinalIgnoreCase) ||
                    args[i].Equals("-c", StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        /// <summary>
        /// 初始化逻辑：验证配置、应用初始设置、生成寄存器文档。
        /// </summary>
        private static async Task RunInitializationAsync(IServiceProvider services)
        {
            var appSettings = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppSettings>>().Value;
            var shared = services.GetRequiredService<SharedData>();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(services.GetRequiredService<IConfiguration>())
                .CreateLogger();

            try
            {
                // 校验 SlaveId 合法性
                if (appSettings.Modbus.SlaveId < 1 || appSettings.Modbus.SlaveId > 247)
                {
                    _log.Error("Invalid Modbus Slave ID: {Id}. Must be between 1 and 247.", appSettings.Modbus.SlaveId);
                    return;
                }

                _log.Information("=== AmVirtualSlave Starting ===");
                _log.Information("Loaded Config: Port={Port}, SlaveId={Id}, Mode={Mode}",
                    appSettings.Modbus.Port,
                    appSettings.Modbus.SlaveId,
                    appSettings.Simulation.InitialMode);

                // 应用初始配置
                if (Enum.TryParse<SharedData.SimulationMode>(appSettings.Simulation.InitialMode, out var mode))
                {
                    shared.SetMode(mode);
                }
                shared.SetNoiseMultiplier(appSettings.Simulation.DefaultNoise);
                shared.SetResponseDelay(appSettings.Simulation.DefaultDelayMs);

                // 生成寄存器映射文档
                _log.Information("=== Generating Register Map documentation ===");
                string markdown = RegisterMap.GenerateMarkdownTable();
                File.WriteAllText("REGISTER_MAP.md", markdown);
                _log.Information("=== Register map saved to REGISTER_MAP.md ===");
            }
            catch (Exception ex)
            {
                _log.Fatal(ex, "Initialization failed");
            }
        }
    }
}
