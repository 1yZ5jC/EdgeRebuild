using System;

namespace EdgeRebuild.Core
{
    /// <summary>搁置组的时间桶（按组创建时间归类）。</summary>
    public enum SweptTimeBucket
    {
        JustNow = 0,
        Today = 1,
        Yesterday = 2,
        Earlier = 3
    }

    /// <summary>
    /// 搁置组时间桶的纯逻辑：不依赖 XAML/WinRT，便于单元测试。
    /// 一小时内优先归入“刚搁置”，跨午夜也保持连续感。
    /// </summary>
    public static class SweptTabTime
    {
        public static SweptTimeBucket GetBucket(DateTime createdAt, DateTime now)
        {
            if (now - createdAt < TimeSpan.FromHours(1))
                return SweptTimeBucket.JustNow;
            if (createdAt.Date == now.Date)
                return SweptTimeBucket.Today;
            if (createdAt.Date == now.Date.AddDays(-1))
                return SweptTimeBucket.Yesterday;
            return SweptTimeBucket.Earlier;
        }

        public static string GetBucketTitle(SweptTimeBucket bucket)
        {
            switch (bucket)
            {
                case SweptTimeBucket.JustNow: return "刚搁置";
                case SweptTimeBucket.Today: return "今天";
                case SweptTimeBucket.Yesterday: return "昨天";
                default: return "更早";
            }
        }
    }
}
