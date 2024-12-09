using CoreRemoting.Toolbox;
using System.Threading.Tasks;
using Xunit;

namespace CoreRemoting.Tests
{
    partial class DisposableTests
    {
        [Fact]
        public async Task AsyncDisposable_executes_action_on_DisposeAsync()
        {
            var disposed = false;

            async Task DisposeTask()
            {
                await Task.Yield();
                disposed = true;
            }

            await using (Disposable.Create(DisposeTask))
                Assert.False(disposed);

            Assert.True(disposed);
        }

        [Fact]
        public async Task AsyncTaskDisposable_executes_action_on_DisposeAsync()
        {
            var disposed = false;

            async ValueTask DisposeAsync()
            {
                await Task.Yield();
                disposed = true;
            }

            await using (Disposable.Create(DisposeAsync))
                Assert.False(disposed);

            Assert.True(disposed);
        }
    }
}