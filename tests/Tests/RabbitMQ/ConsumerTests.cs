using HouseofCat.Dataflows.Pipelines;
using HouseofCat.RabbitMQ;
using HouseofCat.RabbitMQ.WorkState.Extensions;
using HouseofCat.Utilities.File;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HouseofCat.RabbitMQ.Pools;
using HouseofCat.RabbitMQ.Services;
using HouseofCat.RabbitMQ.WorkState;
using IntegrationTests.RabbitMQ;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace RabbitMQ
{
    public class ConsumerTests : IClassFixture<RabbitFixture>
    {
        private readonly RabbitFixture _fixture;
        private static long _processingPaused;

        public ConsumerTests(RabbitFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _fixture.Output = output;
        }

        [Fact]
        public async Task CreateConsumer()
        {
            var options = 
                await JsonFileReader.ReadFileAsync<RabbitOptions>(Path.Combine("RabbitMQ", "TestConfig.json"))
                .ConfigureAwait(false);
            Assert.NotNull(options);

            if (!await _fixture.CheckRabbitHostConnectionAndUpdateFactoryOptions(options).ConfigureAwait(false))
            {
                return;
            }

            var con = new Consumer(options, "TestMessageConsumer");
            Assert.NotNull(con);
            await con.ChannelPool.ShutdownAsync().ConfigureAwait(false);
        }

        [Fact]
        public async Task CreateConsumerAndInitializeChannelPool()
        {
            var options = 
                await JsonFileReader.ReadFileAsync<RabbitOptions>(Path.Combine("RabbitMQ", "TestConfig.json"))
                .ConfigureAwait(false);
            Assert.NotNull(options);

            if (!await _fixture.CheckRabbitHostConnectionAndUpdateFactoryOptions(options).ConfigureAwait(false))
            {
                return;
            }

            var con = new Consumer(new ChannelPool(options), "TestMessageConsumer");
            Assert.NotNull(con);
            await con.ChannelPool.ShutdownAsync().ConfigureAwait(false);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CreateManyConsumersStartAndStop(bool withTopologyRecovery)
        {
            if (!await _fixture.RabbitConnectionCheckAsync.ConfigureAwait(false))
            {
                return;
            }

            var channelPool = await _fixture.GetChannelPoolAsync(withTopologyRecovery).ConfigureAwait(false);
            var topologer = new Topologer(channelPool);
            for (var i = 0; i < 1000; i++)
            {
                var con = new Consumer(channelPool, "TestMessageConsumer");
                await topologer.CreateQueueAsync(
                    con.ConsumerOptions.QueueName, true, false, false, con.ConsumerOptions.QueueArgs)
                    .ConfigureAwait(false);

                await con.StartConsumerAsync().ConfigureAwait(false);
                await con.StopConsumerAsync().ConfigureAwait(false);
            }

            await channelPool.ShutdownAsync().ConfigureAwait(false);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ConsumerStartAndStopTesting(bool withTopologyRecovery)
        {
            if (!await _fixture.RabbitConnectionCheckAsync.ConfigureAwait(false))
            {
                return;
            }

            var service = await _fixture.GetRabbitServiceAsync(withTopologyRecovery).ConfigureAwait(false);
            var consumer = service.GetConsumer("TestMessageConsumer");

            for (var i = 0; i < 100; i++)
            {
                await consumer.StartConsumerAsync().ConfigureAwait(false);
                await consumer.StopConsumerAsync().ConfigureAwait(false);
            }

            await service.ShutdownAsync(true).ConfigureAwait(false);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ConsumerPipelineStartAndStopTesting(bool withTopologyRecovery)
        {
            if (!await _fixture.RabbitConnectionCheckAsync.ConfigureAwait(false))
            {
                return;
            }

            var service = await _fixture.GetRabbitServiceAsync(withTopologyRecovery).ConfigureAwait(false);
            var consumerPipeline = service.CreateConsumerPipeline("TestMessageConsumer", 100, false, BuildPipeline);
            for (var i = 0; i < 100; i++)
            {
                await consumerPipeline.StartAsync(true).ConfigureAwait(false);
                await consumerPipeline.StopAsync().ConfigureAwait(false);
            }

            await service.ShutdownAsync(true).ConfigureAwait(false);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task ConsumerChannelBlockTesting(bool recoverable, bool withTopologyRecovery)
        {
            if (!await _fixture.RabbitConnectionCheckAsync.ConfigureAwait(false))
            {
                return;
            }

            var service =
                recoverable
                    ? await _fixture.GetRecoverableRabbitServiceAsync(withTopologyRecovery).ConfigureAwait(false)
                    : await _fixture.GetRabbitServiceAsync(withTopologyRecovery).ConfigureAwait(false);
            var consumer = service.GetConsumer("TestMessageConsumer");
            await CheckHasUnacknowledgedMessages(
                service,
                recoverable,
                consumer.StartConsumerAsync,
                () => new ValueTask(consumer.ChannelExecutionEngineAsync(TryProcessMessageAsync)));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task ConsumerDirectChannelBlockTesting(bool recoverable, bool withTopologyRecovery)
        {
            if (!await _fixture.RabbitConnectionCheckAsync.ConfigureAwait(false))
            {
                return;
            }

            var service =
                recoverable
                    ? await _fixture.GetRecoverableRabbitServiceAsync(withTopologyRecovery).ConfigureAwait(false)
                    : await _fixture.GetRabbitServiceAsync(withTopologyRecovery).ConfigureAwait(false);
            var consumer = service.GetConsumer("TestMessageConsumer");
            await CheckHasUnacknowledgedMessages(
                service,
                recoverable || !withTopologyRecovery,
                consumer.StartConsumerAsync,
                () => new ValueTask(consumer.DirectChannelExecutionEngineAsync(TryProcessMessageAsync)));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task ConsumerDirectChannelReaderBlockTesting(bool recoverable, bool withTopologyRecovery)
        {
            if (!await _fixture.RabbitConnectionCheckAsync.ConfigureAwait(false))
            {
                return;
            }

            var service =
                recoverable
                    ? await _fixture.GetRecoverableRabbitServiceAsync(withTopologyRecovery).ConfigureAwait(false)
                    : await _fixture.GetRabbitServiceAsync(withTopologyRecovery).ConfigureAwait(false);
            var consumer = service.GetConsumer("TestMessageConsumer");
            await CheckHasUnacknowledgedMessages(
                service,
                recoverable || !withTopologyRecovery,
                consumer.StartConsumerAsync,
                () => consumer.DirectChannelExecutionEngineAsync(ProcessMessageAsync, FinaliseAsync));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task ConsumerPipelineCompletionTesting(bool recoverable, bool withTopologyRecovery)
        {
            if (!await _fixture.RabbitConnectionCheckAsync.ConfigureAwait(false))
            {
                return;
            }

            var service =
                recoverable
                    ? await _fixture.GetRecoverableRabbitServiceAsync(withTopologyRecovery).ConfigureAwait(false)
                    : await _fixture.GetRabbitServiceAsync(withTopologyRecovery).ConfigureAwait(false);
            var consumerPipeline = service.CreateConsumerPipeline("TestMessageConsumer", 100, false, BuildPipeline);
            await CheckHasUnacknowledgedMessages(
                service,
                recoverable || !withTopologyRecovery,
                () => consumerPipeline.StartAsync(true),
                () => new ValueTask(consumerPipeline.AwaitCompletionAsync()),
                () => consumerPipeline.StopAsync(true));
        }

        private async Task CheckHasUnacknowledgedMessages(
            IRabbitService service, bool assertTrue, Func<Task> start, Func<ValueTask> execute, Func<Task> stop = null)
        {
            // reconnectedCount should be 1
            var closeAndPublishTask = RecoverConnectionsThenPublishRandomLetters(service);
            await RunTasks(service, start, execute, closeAndPublishTask, stop).ConfigureAwait(false);
            // this should always be Assert.True without try/catch for a False condition
            if (assertTrue)
            {
                Assert.True(await closeAndPublishTask);
                return;
            }
            try
            {
                Assert.False(await closeAndPublishTask);
            }
            catch (FalseException)
            {
                _fixture.Output.WriteLine("Expected unacknowledged messages but got none (probably a good thing)");
            }
        }

        private static Task RunTasks(
            IRabbitService service, Func<Task> start, Func<ValueTask> execute, Task<bool> publish, Func<Task> stop) =>
            Task.WhenAll(
                start().ContinueWith(_ => execute()),
                publish.ContinueWith(async _ =>
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    if (stop is not null)
                    {
                        await stop().ConfigureAwait(false);
                    }
                    await service.ShutdownAsync(true).ConfigureAwait(false);
                }));

        public async Task<bool> TryProcessMessageAsync(ReceivedData receivedData)
        {
            var state = await ProcessMessageAsync(receivedData).ConfigureAwait(false);
            return state is WorkState { AcknowledgeStepSuccess: true };
        }

        public async Task<IRabbitWorkState> ProcessMessageAsync(ReceivedData receivedData)
        {
            var state = DeserializeStep(receivedData);
            await ProcessStepAsync(state).ConfigureAwait(false);
            await AckMessageAsync(state).ConfigureAwait(false);

            return state;
        }

        private static void PauseProcessing() => Interlocked.CompareExchange(ref _processingPaused, 1, 0);
        private static void ResumeProcessing() => Interlocked.CompareExchange(ref _processingPaused, 0, 1);

        private async Task<bool> RecoverConnectionsThenPublishRandomLetters(IRabbitService service)
        {
            var management = new Management(service.Options.FactoryOptions, _fixture.Output);
            var connections = await management.WaitForConnectionsAndConsumers("TestRabbitServiceQueue", 1)
                .ConfigureAwait(false);

            var recoveredConnections = await management.RecoverConnectionsAndConsumers(
                "TestRabbitServiceQueue", connections, 1, true).ConfigureAwait(false);

            PauseProcessing();

            var prefetch = service.GetConsumer("TestMessageConsumer").ConsumerOptions.BatchSize!.Value;
            var firstBatchCount = prefetch / 2;
            await Task.WhenAll(Enumerable.Range(0, firstBatchCount).Select(_ => PublishRandomLetter(service.Publisher)))
                .ConfigureAwait(false);
            await management.WaitForQueueToHaveUnacknowledgedMessages("TestRabbitServiceQueue", firstBatchCount)
                .ConfigureAwait(false);

            try
            {
                await management.RecoverConnectionsAndConsumers("TestRabbitServiceQueue", recoveredConnections, 1)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                ResumeProcessing();
                return false;
            }

            var remainingCount = prefetch - firstBatchCount + 10;
            await Task.WhenAll(Enumerable.Range(0, remainingCount).Select(_ => PublishRandomLetter(service.Publisher)))
                .ConfigureAwait(false);
            await management.WaitForQueueToHaveUnacknowledgedMessages("TestRabbitServiceQueue", prefetch)
                .ConfigureAwait(false);

            ResumeProcessing();

            var allProcessed = await management.WaitForQueueToHaveNoMessages("TestRabbitServiceQueue", false)
                .ConfigureAwait(false);
            await service.Topologer.DeleteQueueAsync("TestRabbitServiceQueue").ConfigureAwait(false);
            return allProcessed;
        }

        private static Task PublishRandomLetter(IPublisher publisher)
        {
            var letter = MessageExtensions.CreateSimpleRandomLetter("TestRabbitServiceQueue", 2000);
            return publisher.PublishAsync(letter, false);
        }

        private IPipeline<ReceivedData, WorkState> BuildPipeline(int maxDoP, bool? ensureOrdered = null)
        {
            var pipeline = new Pipeline<ReceivedData, WorkState>(
                maxDoP,
                healthCheckInterval: TimeSpan.FromSeconds(10),
                pipelineName: "ConsumerPipelineExample",
                ensureOrdered);

            pipeline.AddStep<ReceivedData, WorkState>(DeserializeStep);
            pipeline.AddAsyncStep<WorkState, WorkState>(ProcessStepAsync);
            pipeline.AddAsyncStep<WorkState, WorkState>(AckMessageAsync);
            pipeline.Finalize(FinaliseAsync);

            return pipeline;
        }

        public class WorkState : RabbitWorkState
        {
            public Letter Letter { get; set; }
            public bool DeserializeStepSuccess { get; set; }
            public bool ProcessStepSuccess { get; set; }
            public bool AcknowledgeStepSuccess { get; set; }
            public bool AllStepsSuccess => DeserializeStepSuccess && ProcessStepSuccess && AcknowledgeStepSuccess;
        }

        private WorkState DeserializeStep(ReceivedData receivedData)
        {
            var state = new WorkState
            {
                ReceivedData = receivedData
            };

            try
            {
                state.Letter = _fixture.SerializationProvider.Deserialize<Letter>(state.ReceivedData.Data);

                if (state.ReceivedData.Data.Length > 0 && state.Letter != null && state.ReceivedData.Letter != null)
                { state.DeserializeStepSuccess = true; }
            }
            catch
            { }

            return state;
        }

        private static async Task<WorkState> ProcessStepAsync(WorkState state)
        {
            await Task.Yield();

            while (Interlocked.Read(ref _processingPaused) == 1)
            {
                await Task.Delay(4).ConfigureAwait(false);
            }

            if (state.DeserializeStepSuccess)
            {
                state.ProcessStepSuccess = true;
            }

            return state;
        }

        private static async Task<WorkState> AckMessageAsync(WorkState state)
        {
            await Task.Yield();

            if (state.ProcessStepSuccess)
            {
                if (state.ReceivedData.AckMessage())
                { state.AcknowledgeStepSuccess = true; }
            }
            else
            {
                if (state.ReceivedData.NackMessage(true))
                { state.AcknowledgeStepSuccess = true; }
            }

            return state;
        }

        private static async Task FinaliseAsync(IRabbitWorkState state)
        {
            await Task.Yield();
            // Lastly mark the excution pipeline finished for this message.
            state.ReceivedData?.Complete(); // This impacts wait to completion step in the Pipeline.
        }
    }
}
