using System;
using System.Collections.Generic;
using System.Text;

namespace AmVritualSlave.Core
{
    /// <summary>
    /// 随机数服务接口：用于替代 new Random()，方便单元测试
    /// </summary>
    public interface IRandomProvider
    {
        double NextDouble();
        int Next(int maxValue);
    }

    /// <summary>
    /// 生产环境实现
    /// </summary>
    public class RandomProvider : IRandomProvider
    {
        private readonly Random _rand = new Random(42);
        public double NextDouble() => _rand.NextDouble();
        public int Next(int max) => _rand.Next(max);
    }
}
