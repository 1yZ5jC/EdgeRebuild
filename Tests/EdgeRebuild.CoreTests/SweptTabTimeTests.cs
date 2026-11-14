using System;
using EdgeRebuild.Core;
using Xunit;

namespace EdgeRebuild.CoreTests
{
    public class SweptTabTimeTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 14, 15, 0, 0);

        [Fact]
        public void GetBucket_WithinOneHour_IsJustNow()
        {
            Assert.Equal(SweptTimeBucket.JustNow,
                SweptTabTime.GetBucket(Now.AddMinutes(-30), Now));
        }

        [Fact]
        public void GetBucket_ExactlyOneHourAgo_IsToday()
        {
            Assert.Equal(SweptTimeBucket.Today,
                SweptTabTime.GetBucket(Now.AddHours(-1), Now));
        }

        [Fact]
        public void GetBucket_EarlierToday_IsToday()
        {
            Assert.Equal(SweptTimeBucket.Today,
                SweptTabTime.GetBucket(new DateTime(2026, 11, 14, 9, 0, 0), Now));
        }

        [Fact]
        public void GetBucket_Yesterday_IsYesterday()
        {
            Assert.Equal(SweptTimeBucket.Yesterday,
                SweptTabTime.GetBucket(new DateTime(2026, 11, 13, 23, 0, 0), Now));
        }

        [Fact]
        public void GetBucket_OlderThanYesterday_IsEarlier()
        {
            Assert.Equal(SweptTimeBucket.Earlier,
                SweptTabTime.GetBucket(new DateTime(2026, 11, 12, 23, 59, 0), Now));
        }

        [Fact]
        public void GetBucket_CrossMidnightWithinOneHour_IsJustNow()
        {
            var now = new DateTime(2026, 11, 14, 0, 30, 0);
            Assert.Equal(SweptTimeBucket.JustNow,
                SweptTabTime.GetBucket(new DateTime(2026, 11, 13, 23, 50, 0), now));
        }

        [Fact]
        public void GetBucket_FutureTimestamp_IsJustNow()
        {
            Assert.Equal(SweptTimeBucket.JustNow,
                SweptTabTime.GetBucket(Now.AddMinutes(10), Now));
        }

        [Theory]
        [InlineData(SweptTimeBucket.JustNow, "刚搁置")]
        [InlineData(SweptTimeBucket.Today, "今天")]
        [InlineData(SweptTimeBucket.Yesterday, "昨天")]
        [InlineData(SweptTimeBucket.Earlier, "更早")]
        public void GetBucketTitle_ReturnsExpectedText(SweptTimeBucket bucket, string expected)
        {
            Assert.Equal(expected, SweptTabTime.GetBucketTitle(bucket));
        }
    }
}
