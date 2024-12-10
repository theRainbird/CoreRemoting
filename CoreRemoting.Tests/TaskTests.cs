using System;
using System.Threading.Tasks;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests
{
    public class TaskTests
    {
        private TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

        [Fact]
        public async Task Timeout_method_doesnt_fail_if_task_completes_in_time()
        {
            var task = Task.Delay(Ms(1));
            await task.Timeout(0.1); // shouldn't throw
        }

        [Fact]
        public async Task Timeout_method_throws_TimeoutException_when_task_times_out()
        {
            var task = Task.Delay(Ms(100));

            var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
                await task.Timeout(0.01,
                    nameof(Timeout_method_throws_TimeoutException_when_task_times_out)));

            Assert.Equal(nameof(Timeout_method_throws_TimeoutException_when_task_times_out), ex.Message);
        }

        [Fact]
        public async Task TimeoutT_method_doesnt_fail_if_task_completes_in_time()
        {
            async Task<string> TestTask()
            {
                await Task.Delay(Ms(1));
                return nameof(TimeoutT_method_doesnt_fail_if_task_completes_in_time);
            }

            var result = await TestTask().Timeout(0.1); // shouldn't throw
            Assert.Equal(nameof(TimeoutT_method_doesnt_fail_if_task_completes_in_time), result);
        }

        [Fact]
        public async Task TimeoutT_method_throws_TimeoutException_when_task_times_out()
        {
            async Task<string> TestTask()
            {
                await Task.Delay(Ms(100));
                return "Fail!";
            }

            var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
                await TestTask().Timeout(0.01,
                    nameof(TimeoutT_method_throws_TimeoutException_when_task_times_out)));

            Assert.Equal(nameof(TimeoutT_method_throws_TimeoutException_when_task_times_out), ex.Message);
        }
    }
}