using System;
using System.Collections.Generic;
using System.Text;

namespace AmVirtualSlave.Core
{
    /// <summary>
    /// 时间服务接口：用于替代 Task.Delay 和 DateTime.UtcNow，方便单元测试
    /// </summary>
    public interface ITimeProvider
    {
        Task Delay(int milliseconds, CancellationToken token);
        DateTime UtcNow { get; }
    }

    /// <summary>
    /// 生产环境实现
    /// </summary>
    public class TimeProvider : ITimeProvider
    {
        public Task Delay(int ms, CancellationToken token) => Task.Delay(ms, token);
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
